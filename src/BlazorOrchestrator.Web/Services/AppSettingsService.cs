using System.Text.Json;
using System.Text.Json.Nodes;
using BlazorDataOrchestrator.Core.Services;

namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for managing application settings stored in appsettings.json
/// with Azure Table Storage via <see cref="SettingsService"/> as the primary store.
/// </summary>
public class AppSettingsService
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly SettingsService _settingsService;
    private readonly string _appSettingsPath;
    private static readonly object _fileLock = new();

    // Cached offset to avoid repeated Azure Table calls on synchronous reads
    private TimeSpan? _cachedOffset;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public AppSettingsService(IConfiguration configuration, IWebHostEnvironment environment, SettingsService settingsService)
    {
        _configuration = configuration;
        _environment = environment;
        _settingsService = settingsService;
        _appSettingsPath = Path.Combine(_environment.ContentRootPath, "appsettings.json");
    }

    /// <summary>
    /// Gets the configured timezone offset synchronously from cache.
    /// If cache is empty, falls back to IConfiguration, then default -08:00.
    /// Use <see cref="GetTimezoneOffsetAsync"/> for the full Azure Table → config → default fallback.
    /// </summary>
    public TimeSpan GetTimezoneOffset()
    {
        if (_cachedOffset.HasValue && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedOffset.Value;
        }

        // Synchronous fallback: IConfiguration → default
        var offsetString = _configuration["TimezoneOffset"] ?? "-08:00";
        if (TryParseTimezoneOffset(offsetString, out var offset))
        {
            return offset;
        }
        return TimeSpan.FromHours(-8); // Default to Pacific Time
    }

    /// <summary>
    /// Gets the configured timezone offset asynchronously.
    /// Fallback chain: Azure Table Storage → IConfiguration → hardcoded default.
    /// </summary>
    public async Task<TimeSpan> GetTimezoneOffsetAsync()
    {
        if (_cachedOffset.HasValue && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedOffset.Value;
        }

        string? offsetString = null;

        // 1. Try Azure Table Storage
        try
        {
            offsetString = await _settingsService.GetAsync("TimezoneOffset");
        }
        catch
        {
            // Swallow — fall through to config
        }

        // 2. Fallback to IConfiguration
        if (string.IsNullOrEmpty(offsetString))
        {
            offsetString = _configuration["TimezoneOffset"];
        }

        // 3. Fallback to hardcoded default
        if (string.IsNullOrEmpty(offsetString))
        {
            offsetString = "-08:00";
        }

        if (TryParseTimezoneOffset(offsetString, out var offset))
        {
            _cachedOffset = offset;
            _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
            return offset;
        }

        return TimeSpan.FromHours(-8);
    }

    /// <summary>
    /// Gets the timezone offset as a formatted string (e.g., "-08:00")
    /// </summary>
    public string GetTimezoneOffsetString()
    {
        var offset = GetTimezoneOffset();
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Sets the timezone offset — writes to both Azure Table Storage and appsettings.json.
    /// </summary>
    public async Task SetTimezoneOffsetAsync(string offsetString)
    {
        if (!TryParseTimezoneOffset(offsetString, out _))
        {
            throw new ArgumentException($"Invalid timezone offset format: {offsetString}. Expected format: +HH:MM or -HH:MM");
        }

        // Write to Azure Table Storage (primary)
        try
        {
            await _settingsService.SetAsync("TimezoneOffset", offsetString, "Display timezone offset");
        }
        catch
        {
            // If Azure Table write fails, still write to file
        }

        // Write to appsettings.json (backward compatibility)
        await UpdateAppSettingsAsync("TimezoneOffset", offsetString);

        // Invalidate cache so next read picks up the new value
        _cachedOffset = null;
        _cacheExpiry = DateTime.MinValue;
    }

    /// <summary>
    /// Gets a list of common timezone offsets for display in a dropdown
    /// </summary>
    public List<TimezoneOption> GetAvailableTimezones()
    {
        return new List<TimezoneOption>
        {
            new("-12:00", "UTC-12:00 (Baker Island)"),
            new("-11:00", "UTC-11:00 (American Samoa)"),
            new("-10:00", "UTC-10:00 (Hawaii)"),
            new("-09:00", "UTC-09:00 (Alaska)"),
            new("-08:00", "UTC-08:00 (Pacific Time)"),
            new("-07:00", "UTC-07:00 (Mountain Time)"),
            new("-06:00", "UTC-06:00 (Central Time)"),
            new("-05:00", "UTC-05:00 (Eastern Time)"),
            new("-04:00", "UTC-04:00 (Atlantic Time)"),
            new("-03:00", "UTC-03:00 (Buenos Aires)"),
            new("-02:00", "UTC-02:00 (Mid-Atlantic)"),
            new("-01:00", "UTC-01:00 (Azores)"),
            new("+00:00", "UTC+00:00 (UTC/London)"),
            new("+01:00", "UTC+01:00 (Central Europe)"),
            new("+02:00", "UTC+02:00 (Eastern Europe)"),
            new("+03:00", "UTC+03:00 (Moscow)"),
            new("+04:00", "UTC+04:00 (Dubai)"),
            new("+05:00", "UTC+05:00 (Pakistan)"),
            new("+05:30", "UTC+05:30 (India)"),
            new("+06:00", "UTC+06:00 (Bangladesh)"),
            new("+07:00", "UTC+07:00 (Bangkok)"),
            new("+08:00", "UTC+08:00 (Singapore/China)"),
            new("+09:00", "UTC+09:00 (Japan/Korea)"),
            new("+10:00", "UTC+10:00 (Sydney)"),
            new("+11:00", "UTC+11:00 (Solomon Islands)"),
            new("+12:00", "UTC+12:00 (New Zealand)"),
            new("+13:00", "UTC+13:00 (Tonga)"),
            new("+14:00", "UTC+14:00 (Line Islands)")
        };
    }

    /// <summary>
    /// Parses a timezone offset string (e.g., "-08:00" or "+05:30") into a TimeSpan
    /// </summary>
    public static bool TryParseTimezoneOffset(string offsetString, out TimeSpan offset)
    {
        offset = TimeSpan.Zero;
        
        if (string.IsNullOrWhiteSpace(offsetString))
            return false;

        offsetString = offsetString.Trim();
        
        // Handle both +/-HH:MM and HH:MM formats
        var isNegative = offsetString.StartsWith("-");
        var cleanOffset = offsetString.TrimStart('+', '-');

        if (TimeSpan.TryParse(cleanOffset, out var parsedOffset))
        {
            offset = isNegative ? -parsedOffset : parsedOffset;
            return true;
        }

        return false;
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
/// Represents a timezone option for display in dropdowns
/// </summary>
public record TimezoneOption(string Value, string Label);
