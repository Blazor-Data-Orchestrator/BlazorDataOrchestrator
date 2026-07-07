using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Azure.Data.Tables;
using BlazorOrchestrator.Web.Data.Data;
using Microsoft.AspNetCore.Components.Authorization;

namespace BlazorOrchestrator.Web.Services;

public class CodeChangeLogService
{
    private readonly TableServiceClient _tableServiceClient;
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly ILogger<CodeChangeLogService> _logger;
    private readonly IConfiguration _configuration;
    private const string SnapshotTableName = "CodeChangeSnapshots";

    public CodeChangeLogService(
        TableServiceClient tableServiceClient,
        AuthenticationStateProvider authStateProvider,
        ILogger<CodeChangeLogService> logger,
        IConfiguration configuration)
    {
        _tableServiceClient = tableServiceClient;
        _authStateProvider = authStateProvider;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task LogCodeChangeAsync(
        int jobId,
        string fileName,
        string language,
        string changeType,
        string content,
        string? previousContent,
        string? summary)
    {
        if (!IsEnabled()) return;

        try
        {
            var (userId, userName) = await GetCurrentUserAsync();
            if (string.IsNullOrEmpty(userId)) return;

            var contentHash = ComputeHash(content);
            var (linesAdded, linesRemoved) = ComputeLineDiff(previousContent, content);

            if (content.Length <= GetMaxSnapshotSize())
            {
                await WriteSnapshotAsync(jobId, fileName, content, contentHash, userId, changeType,
                    userName, language, summary, linesAdded, linesRemoved);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log code change for job {JobId}, file {FileName}", jobId, fileName);
        }
    }

    public async Task<List<JobCodeChangeLog>> GetChangeHistoryAsync(int jobId, int? take = 20, int? skip = 0)
    {
        var results = new List<JobCodeChangeLog>();
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(SnapshotTableName);
            var partitionKey = jobId.ToString();

            var entities = tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}'",
                select: new[] { "RowKey", "FileName", "ChangeType", "UserId", "UserName", "Language",
                                "Summary", "LinesAdded", "LinesRemoved", "ContentHash", "Timestamp" });

            await foreach (var entity in entities)
            {
                results.Add(MapEntityToChangeLog(entity, jobId));
            }

            // Sort by CreatedDate descending (newest first)
            results = results.OrderByDescending(r => r.CreatedDate).ToList();

            // Apply skip/take
            if (skip.HasValue && skip.Value > 0)
                results = results.Skip(skip.Value).ToList();
            if (take.HasValue)
                results = results.Take(take.Value).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get change history for job {JobId}", jobId);
        }

        return results;
    }

    public async Task<string?> GetSnapshotContentAsync(int jobId, string rowKey)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(SnapshotTableName);
            var partitionKey = jobId.ToString();
            var response = await tableClient.GetEntityAsync<TableEntity>(partitionKey, rowKey);
            return response?.Value?.GetString("Content");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get snapshot {RowKey} for job {JobId}", rowKey, jobId);
            return null;
        }
    }

    public async Task<DiffResult> GetDiffAsync(int jobId, string olderRowKey, string newerRowKey)
    {
        var olderContent = await GetSnapshotContentAsync(jobId, olderRowKey) ?? "";
        var newerContent = await GetSnapshotContentAsync(jobId, newerRowKey) ?? "";

        var olderLines = olderContent.Split('\n');
        var newerLines = newerContent.Split('\n');

        var diffLines = new List<DiffLine>();
        int linesAdded = 0, linesRemoved = 0;

        // Simple LCS-based diff
        var lcs = ComputeLcs(olderLines, newerLines);
        int li = olderLines.Length, lj = newerLines.Length, lk = lcs.Count - 1;

        var tempLines = new List<DiffLine>();

        while (li > 0 || lj > 0)
        {
            if (li > 0 && lj > 0 && lk >= 0 && olderLines[li - 1] == newerLines[lj - 1] && olderLines[li - 1] == lcs[lk])
            {
                tempLines.Add(new DiffLine { Type = DiffLineType.Unchanged, Content = olderLines[li - 1], OldLineNumber = li, NewLineNumber = lj });
                li--; lj--; lk--;
            }
            else if (lj > 0 && (li == 0 || (lk >= 0 && newerLines[lj - 1] != lcs[lk])))
            {
                tempLines.Add(new DiffLine { Type = DiffLineType.Added, Content = newerLines[lj - 1], NewLineNumber = lj });
                lj--;
                linesAdded++;
            }
            else
            {
                tempLines.Add(new DiffLine { Type = DiffLineType.Removed, Content = olderLines[li - 1], OldLineNumber = li });
                li--;
                linesRemoved++;
            }
        }

        tempLines.Reverse();

        return new DiffResult
        {
            OlderContent = olderContent,
            NewerContent = newerContent,
            Lines = tempLines,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved
        };
    }

    public async Task<int> GetChangeCountAsync(int jobId)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(SnapshotTableName);
            var partitionKey = jobId.ToString();
            int count = 0;

            var entities = tableClient.QueryAsync<TableEntity>(
                filter: $"PartitionKey eq '{partitionKey}'",
                select: new[] { "RowKey" });

            await foreach (var _ in entities)
            {
                count++;
            }

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get change count for job {JobId}", jobId);
            return 0;
        }
    }

    private async Task WriteSnapshotAsync(
        int jobId, string fileName, string content, string contentHash, string userId, string changeType,
        string userName, string language, string? summary, int linesAdded, int linesRemoved)
    {
        try
        {
            var tableClient = _tableServiceClient.GetTableClient(SnapshotTableName);
            await tableClient.CreateIfNotExistsAsync();

            var partitionKey = jobId.ToString();

            // Deduplication check
            if (ShouldDeduplicate())
            {
                var existingEntities = tableClient.QueryAsync<TableEntity>(
                    filter: $"PartitionKey eq '{partitionKey}' and FileName eq '{fileName}'",
                    maxPerPage: 1,
                    select: new[] { "RowKey", "ContentHash" });

                await foreach (var entity in existingEntities)
                {
                    if (entity.GetString("ContentHash") == contentHash)
                    {
                        return; // Duplicate content, skip
                    }
                    break; // Only check the most recent
                }
            }

            var rowKey = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";

            var snapshotEntity = new TableEntity(partitionKey, rowKey)
            {
                { "FileName", fileName },
                { "Content", content },
                { "ContentHash", contentHash },
                { "UserId", userId },
                { "ChangeType", changeType },
                { "UserName", userName },
                { "Language", language },
                { "Summary", summary?.Length > 1000 ? summary[..1000] : summary },
                { "LinesAdded", linesAdded },
                { "LinesRemoved", linesRemoved }
            };

            await tableClient.AddEntityAsync(snapshotEntity);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write snapshot for job {JobId}, file {FileName}", jobId, fileName);
        }
    }

    private static JobCodeChangeLog MapEntityToChangeLog(TableEntity entity, int jobId)
    {
        // Parse CreatedDate from RowKey (format: yyyyMMddHHmmssfff_guid) or fall back to Timestamp
        var createdDate = entity.Timestamp?.UtcDateTime ?? DateTime.UtcNow;
        var rowKey = entity.RowKey ?? "";
        if (rowKey.Length >= 17 && DateTime.TryParseExact(
                rowKey[..17], "yyyyMMddHHmmssfff",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsedDate))
        {
            createdDate = parsedDate;
        }

        return new JobCodeChangeLog
        {
            JobId = jobId,
            SnapshotRowKey = rowKey,
            FileName = entity.GetString("FileName") ?? "",
            ChangeType = entity.GetString("ChangeType") ?? "Unknown",
            UserId = entity.GetString("UserId") ?? "",
            UserName = entity.GetString("UserName") ?? "Unknown",
            Language = entity.GetString("Language") ?? "",
            Summary = entity.GetString("Summary") ?? "",
            LinesAdded = entity.GetInt32("LinesAdded") ?? 0,
            LinesRemoved = entity.GetInt32("LinesRemoved") ?? 0,
            CreatedDate = createdDate
        };
    }

    private async Task<(string userId, string userName)> GetCurrentUserAsync()
    {
        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var user = authState.User;
            if (user?.Identity?.IsAuthenticated != true)
                return (string.Empty, string.Empty);

            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            var userName = user.FindFirst(ClaimTypes.Name)?.Value ?? user.Identity.Name ?? "Unknown";
            return (userId, userName);
        }
        catch
        {
            return (string.Empty, string.Empty);
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexStringLower(bytes);
    }

    private static (int linesAdded, int linesRemoved) ComputeLineDiff(string? previousContent, string currentContent)
    {
        if (string.IsNullOrEmpty(previousContent))
            return (currentContent.Split('\n').Length, 0);

        var oldLines = new HashSet<string>(previousContent.Split('\n'));
        var newLines = new HashSet<string>(currentContent.Split('\n'));

        var added = newLines.Except(oldLines).Count();
        var removed = oldLines.Except(newLines).Count();

        return (added, removed);
    }

    private static List<string> ComputeLcs(string[] a, string[] b)
    {
        int m = a.Length, n = b.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var result = new List<string>();
        int x = m, y = n;
        while (x > 0 && y > 0)
        {
            if (a[x - 1] == b[y - 1]) { result.Add(a[x - 1]); x--; y--; }
            else if (dp[x - 1, y] > dp[x, y - 1]) x--;
            else y--;
        }
        result.Reverse();
        return result;
    }

    private bool IsEnabled() =>
        _configuration.GetValue("CodeChangeLogging:Enabled", true);

    private int GetMaxSnapshotSize() =>
        _configuration.GetValue("CodeChangeLogging:MaxSnapshotSizeBytes", 1048576);

    private bool ShouldDeduplicate() =>
        _configuration.GetValue("CodeChangeLogging:DeduplicateSnapshots", true);
}

public class DiffResult
{
    public string OlderContent { get; set; } = "";
    public string NewerContent { get; set; } = "";
    public List<DiffLine> Lines { get; set; } = new();
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
}

public class DiffLine
{
    public DiffLineType Type { get; set; }
    public string Content { get; set; } = "";
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
}

public enum DiffLineType
{
    Added,
    Removed,
    Unchanged
}
