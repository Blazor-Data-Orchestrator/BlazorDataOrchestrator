using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for managing application settings stored in appsettings.json
/// with Azure Table Storage via <see cref="SettingsService"/> as the primary store.
/// Uses proper TimeZoneInfo for DST-aware timezone conversions.
/// </summary>
public class AppSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly SettingsService _settingsService;
    private readonly string _appSettingsPath;
    private static readonly object _fileLock = new();

    // Cached TimeZoneInfo to avoid repeated Azure Table calls on synchronous reads
    private TimeZoneInfo? _cachedTimeZone;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private const string DefaultTimezoneId = "America/Los_Angeles";

    public AppSettingsService(IConfiguration configuration, IWebHostEnvironment environment, SettingsService settingsService)
    {
        _configuration = configuration;
        _environment = environment;
        _settingsService = settingsService;
        _appSettingsPath = Path.Combine(_environment.ContentRootPath, "appsettings.json");
    }

    /// <summary>
    /// Gets the configured TimeZoneInfo synchronously from cache.
    /// Delegates to <see cref="GetTimeZoneInfoAsync"/> (Azure Table → config → default)
    /// on cache miss, running on a threadpool thread to avoid Blazor deadlocks.
    /// </summary>
    public TimeZoneInfo GetTimeZoneInfo()
    {
        if (_cachedTimeZone != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedTimeZone;
        }

        // Delegate to the async version (Azure Table → config → default) on a threadpool thread
        try
        {
            return Task.Run(() => GetTimeZoneInfoAsync()).GetAwaiter().GetResult();
        }
        catch
        {
            // If the async path fails entirely, fall back to IConfiguration → default
            var timezoneId = _configuration["TimezoneId"] ?? DefaultTimezoneId;
            var tz = ResolveTimeZone(timezoneId);
            _cachedTimeZone = tz;
            _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
            return tz;
        }
    }

    /// <summary>
    /// Gets the configured TimeZoneInfo asynchronously.
    /// Fallback chain: Azure Table Storage → IConfiguration → hardcoded default.
    /// </summary>
    public async Task<TimeZoneInfo> GetTimeZoneInfoAsync()
    {
        if (_cachedTimeZone != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedTimeZone;
        }

        string? timezoneId = null;

        // 1. Try Azure Table Storage (primary — "TimezoneId")
        try
        {
            timezoneId = await _settingsService.GetAsync("TimezoneId");
        }
        catch
        {
            // Swallow — fall through to config
        }

        // 2. Fallback to IConfiguration
        if (string.IsNullOrEmpty(timezoneId))
        {
            timezoneId = _configuration["TimezoneId"];
        }

        // 3. Fallback to hardcoded default
        if (string.IsNullOrEmpty(timezoneId))
        {
            timezoneId = DefaultTimezoneId;
        }

        var tz = ResolveTimeZone(timezoneId);
        _cachedTimeZone = tz;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
        return tz;
    }

    /// <summary>
    /// Gets the current UTC offset for the configured timezone (DST-aware).
    /// </summary>
    public TimeSpan GetTimezoneOffset()
    {
        var tz = GetTimeZoneInfo();
        return tz.GetUtcOffset(DateTime.UtcNow);
    }

    /// <summary>
    /// Gets the current UTC offset string for the configured timezone (e.g., "-07:00" during PDT).
    /// </summary>
    public string GetTimezoneOffsetString()
    {
        var offset = GetTimezoneOffset();
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Gets the configured timezone ID (e.g., "America/Los_Angeles").
    /// </summary>
    public string GetTimezoneId()
    {
        var tz = GetTimeZoneInfo();
        return tz.Id;
    }

    /// <summary>
    /// Sets the timezone — writes to both Azure Table Storage and appsettings.json.
    /// </summary>
    public async Task SetTimezoneAsync(string timezoneId)
    {
        // Validate that the timezone ID is recognized
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException($"Invalid timezone ID: {timezoneId}.");
        }

        // Write to Azure Table Storage (primary)
        await _settingsService.SetAsync("TimezoneId", timezoneId, "Display timezone (IANA ID, DST-aware)");

        // Write to appsettings.json (best-effort backward compatibility for local dev)
        try
        {
            await UpdateAppSettingsAsync("TimezoneId", timezoneId);
        }
        catch
        {
            // File write may fail in production containers where the filesystem is read-only
        }

        // Invalidate cache so next read picks up the new value
        _cachedTimeZone = null;
        _cacheExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Gets a list of common timezones for display in a dropdown.
    /// Uses IANA timezone IDs that handle DST automatically.
    /// </summary>
    public List<TimezoneOption> GetAvailableTimezones()
    {
        return new List<TimezoneOption>
        {
            new("Etc/GMT+12", "UTC-12:00 (Baker Island)"),
            new("Pacific/Pago_Pago", "UTC-11:00 (American Samoa)"),
            new("Pacific/Honolulu", "UTC-10:00 (Hawaii)"),
            new("America/Anchorage", "UTC-09:00 (Alaska)"),
            new("America/Los_Angeles", "Pacific Time (US & Canada)"),
            new("America/Denver", "Mountain Time (US & Canada)"),
            new("America/Chicago", "Central Time (US & Canada)"),
            new("America/New_York", "Eastern Time (US & Canada)"),
            new("America/Halifax", "Atlantic Time (Canada)"),
            new("America/Argentina/Buenos_Aires", "Buenos Aires"),
            new("Atlantic/South_Georgia", "Mid-Atlantic"),
            new("Atlantic/Azores", "Azores"),
            new("Europe/London", "London / UTC"),
            new("Europe/Berlin", "Central European Time"),
            new("Europe/Bucharest", "Eastern European Time"),
            new("Europe/Moscow", "Moscow"),
            new("Asia/Dubai", "Dubai / Gulf"),
            new("Asia/Karachi", "Pakistan"),
            new("Asia/Kolkata", "India"),
            new("Asia/Dhaka", "Bangladesh"),
            new("Asia/Bangkok", "Bangkok / Indochina"),
            new("Asia/Singapore", "Singapore / China"),
            new("Asia/Tokyo", "Japan / Korea"),
            new("Australia/Sydney", "Sydney / AEST"),
            new("Pacific/Guadalcanal", "Solomon Islands"),
            new("Pacific/Auckland", "New Zealand"),
            new("Pacific/Tongatapu", "Tonga"),
            new("Pacific/Kiritimati", "Line Islands")
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fallback to default if the stored ID is invalid
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimezoneId);
            }
            catch
            {
                return TimeZoneInfo.Utc;
            }
        }
    }

    private async Task UpdateAppSettingsAsync(string key, string value)
    {
        lock (_fileLock)
        {
            var json = File.ReadAllText(_appSettingsPath);
            var jsonNode = JsonNode.Parse(json) ?? new JsonObject();
            
            jsonNode[key] = value;

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true 
            };
            
            var updatedJson = jsonNode.ToJsonString(options);
            File.WriteAllText(_appSettingsPath, updatedJson);
        }

        // Allow the file system to complete the write
        await Task.Delay(100);
    }
}

/// <summary>
/// Represents a timezone option for display in dropdowns.
/// Value is an IANA timezone ID (e.g., "America/Los_Angeles").
/// </summary>
public record TimezoneOption(string Value, string Label);
