using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Configuration;
using BlazorDataOrchestrator.Core.Models;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Resolves the effective appsettings for a job: base file deep-merged with the
/// environment overlay, with the four reserved connection strings applied last.
/// </summary>
public static class AppSettingsResolver
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = false };

    /// <summary>
    /// Resolves settings from an extracted package directory.
    /// </summary>
    public static AppSettingsResolution Resolve(string directory, string environment, ReservedConnectionStrings reserved)
    {
        var result = new AppSettingsResolution();
        var normalized = JobEnvironments.Normalize(environment);
        var overlayFileName = JobEnvironments.GetFileName(normalized);

        var basePath = FindFile(directory, JobEnvironments.BaseFileName);
        var overlayPath = FindFile(directory, overlayFileName);

        if (basePath == null && overlayPath == null)
        {
            result.IsFatal = true;
            result.FatalReason =
                $"No appsettings found in package. Expected '{JobEnvironments.BaseFileName}' and/or '{overlayFileName}'.";
            return result;
        }

        JsonObject? baseNode = null;
        if (basePath != null)
        {
            baseNode = TryParse(File.ReadAllText(basePath));
            if (baseNode == null)
                result.Warnings.Add($"'{JobEnvironments.BaseFileName}' is not valid JSON and was ignored.");
            else
                result.BaseFileUsed = JobEnvironments.BaseFileName;
        }
        else
        {
            result.Warnings.Add($"'{JobEnvironments.BaseFileName}' not found in package; using the '{normalized}' overlay alone.");
        }

        JsonObject? overlayNode = null;
        if (overlayPath != null)
        {
            overlayNode = TryParse(File.ReadAllText(overlayPath));
            if (overlayNode == null)
                result.Warnings.Add($"'{overlayFileName}' is not valid JSON and was ignored.");
            else
                result.OverlayFileUsed = overlayFileName;
        }
        else
        {
            result.Warnings.Add($"'{overlayFileName}' not found in package; using '{JobEnvironments.BaseFileName}' alone.");
        }

        if (baseNode == null && overlayNode == null)
        {
            result.IsFatal = true;
            result.FatalReason =
                $"No usable appsettings found in package. Expected valid JSON in '{JobEnvironments.BaseFileName}' or '{overlayFileName}'.";
            return result;
        }

        var merged = baseNode ?? new JsonObject();
        if (overlayNode != null)
        {
            DeepMerge(merged, overlayNode);
        }

        if (!reserved.TryValidate(out var missing))
        {
            result.IsFatal = true;
            result.FatalReason =
                $"The host did not supply values for the reserved connection string(s): {string.Join(", ", missing)}.";
            return result;
        }

        ApplyReserved(merged, reserved);
        result.Json = merged.ToJsonString(WriteOptions);
        return result;
    }

    /// <summary>
    /// Overwrites the four reserved connection strings in a settings JSON document.
    /// All other content is preserved. Invalid JSON is returned unchanged.
    /// </summary>
    public static string ApplyReserved(string json, ReservedConnectionStrings reserved)
    {
        var node = TryParse(json);
        if (node == null)
            return json;

        ApplyReserved(node, reserved);
        return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// Deep merges two JSON documents. Objects merge recursively; scalars and arrays are replaced.
    /// </summary>
    public static string DeepMerge(string baseJson, string overlayJson)
    {
        var baseNode = TryParse(baseJson) ?? new JsonObject();
        var overlayNode = TryParse(overlayJson);
        if (overlayNode != null)
            DeepMerge(baseNode, overlayNode);
        return baseNode.ToJsonString(WriteOptions);
    }

    private static void ApplyReserved(JsonObject root, ReservedConnectionStrings reserved)
    {
        if (root["ConnectionStrings"] is not JsonObject connectionStrings)
        {
            connectionStrings = new JsonObject();
            root["ConnectionStrings"] = connectionStrings;
        }

        foreach (var kv in reserved.ToDictionary())
        {
            // Match case-insensitively so a differently-cased packaged key is replaced, not duplicated.
            var existingKey = connectionStrings
                .Select(p => p.Key)
                .FirstOrDefault(k => string.Equals(k, kv.Key, StringComparison.OrdinalIgnoreCase));

            if (existingKey != null && existingKey != kv.Key)
                connectionStrings.Remove(existingKey);

            connectionStrings[kv.Key] = kv.Value;
        }
    }

    private static void DeepMerge(JsonObject target, JsonObject overlay)
    {
        foreach (var property in overlay.ToList())
        {
            if (property.Value is JsonObject overlayChild && target[property.Key] is JsonObject targetChild)
            {
                DeepMerge(targetChild, overlayChild);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static JsonObject? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Finds the shallowest match for a file name under the given directory.</summary>
    private static string? FindFile(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.GetFiles(directory, fileName, SearchOption.AllDirectories)
            .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
