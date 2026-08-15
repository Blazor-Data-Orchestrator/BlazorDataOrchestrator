using System.IO.Compression;
using BlazorDataOrchestrator.Core.Configuration;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Rewrites the four reserved connection strings inside every appsettings file of a
/// job package, and creates any missing environment overlay files.
/// </summary>
public static class PackageAppSettingsStamper
{
    private const long MaxTotalUncompressedBytes = 256L * 1024 * 1024;
    private const int MaxEntryCount = 5000;
    private const long MaxEntryBytes = 16L * 1024 * 1024;

    public sealed class StampResult
    {
        public List<string> StampedFiles { get; } = new();
        public List<string> CreatedFiles { get; } = new();
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Stamps the reserved connection strings into the supplied package.
    /// Returns a new seekable stream positioned at zero; the caller owns it.
    /// </summary>
    public static async Task<(MemoryStream Stream, StampResult Result)> StampAsync(
        Stream packageStream,
        ReservedConnectionStrings reserved)
    {
        var result = new StampResult();

        var buffer = new MemoryStream();
        if (packageStream.CanSeek)
            packageStream.Position = 0;
        await packageStream.CopyToAsync(buffer);
        buffer.Position = 0;

        try
        {
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            {
                if (!Validate(archive, result))
                {
                    buffer.Position = 0;
                    return (buffer, result);
                }

                string? codeFolder = null;
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in archive.Entries.ToList())
                {
                    var name = Path.GetFileName(entry.FullName);
                    if (!JobEnvironments.AllFileNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        continue;

                    present.Add(name);
                    codeFolder ??= GetDirectory(entry.FullName);

                    string content;
                    using (var reader = new StreamReader(entry.Open()))
                    {
                        content = await reader.ReadToEndAsync();
                    }

                    var stamped = AppSettingsResolver.ApplyReserved(content, reserved);
                    if (stamped == content && !LooksLikeJson(content))
                    {
                        result.Warnings.Add($"'{entry.FullName}' is not valid JSON and was left unchanged.");
                        continue;
                    }

                    using var writeStream = entry.Open();
                    writeStream.SetLength(0);
                    using var writer = new StreamWriter(writeStream);
                    await writer.WriteAsync(stamped);
                    result.StampedFiles.Add(entry.FullName);
                }

                // Create any missing appsettings files so the runtime always has a valid overlay.
                codeFolder ??= GuessCodeFolder(archive);
                var seed = AppSettingsResolver.ApplyReserved("{}", reserved);

                foreach (var fileName in JobEnvironments.AllFileNames)
                {
                    if (present.Contains(fileName))
                        continue;

                    var path = string.IsNullOrEmpty(codeFolder) ? fileName : $"{codeFolder}/{fileName}";
                    var newEntry = archive.CreateEntry(path);
                    using var writer = new StreamWriter(newEntry.Open());
                    await writer.WriteAsync(seed);
                    result.CreatedFiles.Add(path);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            result.Warnings.Add($"Package could not be opened as an archive and was stored unmodified: {ex.Message}");
        }

        buffer.Position = 0;
        return (buffer, result);
    }

    private static bool Validate(ZipArchive archive, StampResult result)
    {
        if (archive.Entries.Count > MaxEntryCount)
        {
            result.Warnings.Add($"Package has {archive.Entries.Count} entries, exceeding the limit of {MaxEntryCount}. Stamping skipped.");
            return false;
        }

        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var full = entry.FullName.Replace('\\', '/');
            if (full.Split('/').Any(segment => segment == ".."))
            {
                result.Warnings.Add($"Package contains a path-traversal entry '{entry.FullName}'. Stamping skipped.");
                return false;
            }

            if (entry.Length > MaxEntryBytes)
            {
                result.Warnings.Add($"Package entry '{entry.FullName}' exceeds the per-entry size limit. Stamping skipped.");
                return false;
            }

            total += entry.Length;
            if (total > MaxTotalUncompressedBytes)
            {
                result.Warnings.Add("Package uncompressed size exceeds the allowed limit. Stamping skipped.");
                return false;
            }
        }

        return true;
    }

    private static bool LooksLikeJson(string content)
    {
        var trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }

    private static string GetDirectory(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? string.Empty : normalized[..idx];
    }

    /// <summary>Falls back to the folder holding the main code file, then to the content root.</summary>
    private static string GuessCodeFolder(ZipArchive archive)
    {
        var mainEntry = archive.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), "main.cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(e.FullName), "main.py", StringComparison.OrdinalIgnoreCase));

        return mainEntry != null ? GetDirectory(mainEntry.FullName) : "contentFiles/any/any/CodeCSharp";
    }
}
