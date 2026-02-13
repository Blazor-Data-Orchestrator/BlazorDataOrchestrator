using Microsoft.Extensions.Configuration;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Shared service for formatting DateTime values with the configured timezone offset.
/// Reads from Azure Table Storage via <see cref="SettingsService"/>, with fallback to
/// <see cref="IConfiguration"/> and a hardcoded default of -08:00.
/// </summary>
public class TimeDisplayService
{
    private readonly SettingsService _settingsService;
    private readonly IConfiguration _configuration;
    private const string SettingKey = "TimezoneOffset";
    private const string DefaultOffset = "-08:00";

    // Cached offset to avoid repeated Azure Table calls
    private TimeSpan? _cachedOffset;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public TimeDisplayService(SettingsService settingsService, IConfiguration configuration)
    {
        _settingsService = settingsService;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the timezone offset asynchronously, using cache when available.
    /// Fallback chain: Azure Table → IConfiguration → hardcoded default.
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
            offsetString = await _settingsService.GetAsync(SettingKey);
        }
        catch
        {
            // Swallow — fall through to config
        }

        // 2. Fallback to IConfiguration
        if (string.IsNullOrEmpty(offsetString))
        {
            offsetString = _configuration[SettingKey];
        }

        // 3. Fallback to hardcoded default
        if (string.IsNullOrEmpty(offsetString))
        {
            offsetString = DefaultOffset;
        }

        var offset = ParseOffset(offsetString);
        _cachedOffset = offset;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);

        return offset;
    }

    /// <summary>
    /// Gets the timezone offset synchronously from cache.
    /// If the cache is not populated yet, returns the IConfiguration value or default.
    /// </summary>
    public TimeSpan GetTimezoneOffset()
    {
        if (_cachedOffset.HasValue && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedOffset.Value;
        }

        // Synchronous fallback: IConfiguration → default
        var offsetString = _configuration[SettingKey] ?? DefaultOffset;
        return ParseOffset(offsetString);
    }

    /// <summary>
    /// Converts a UTC DateTime to display time using the cached timezone offset.
    /// </summary>
    public DateTime ConvertToDisplayTime(DateTime utcTime)
    {
        var offset = GetTimezoneOffset();
        return utcTime.Add(offset);
    }

    /// <summary>
    /// Formats a DateTime by applying the timezone offset.
    /// </summary>
    public string FormatDateTime(DateTime dateTime, string format = "g")
    {
        var displayTime = ConvertToDisplayTime(dateTime);
        return displayTime.ToString(format);
    }

    /// <summary>
    /// Formats a nullable DateTime by applying the timezone offset.
    /// </summary>
    public string FormatDateTime(DateTime? dateTime, string format = "g", string nullDisplay = "-")
    {
        if (!dateTime.HasValue)
            return nullDisplay;
        return FormatDateTime(dateTime.Value, format);
    }

    /// <summary>
    /// Gets the current display time (UTC + offset).
    /// </summary>
    public DateTime GetCurrentDisplayTime()
    {
        return ConvertToDisplayTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Formats the current time using the timezone offset.
    /// </summary>
    public string FormatCurrentTime(string format = "g")
    {
        return FormatDateTime(DateTime.UtcNow, format);
    }

    /// <summary>
    /// Gets the timezone offset as a formatted string (e.g., "-08:00").
    /// </summary>
    public string GetCurrentTimezoneDisplay()
    {
        var offset = GetTimezoneOffset();
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Invalidates the cached offset so the next read fetches from Azure Table Storage.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedOffset = null;
        _cacheExpiry = DateTime.MinValue;
    }

    private static TimeSpan ParseOffset(string offsetString)
    {
        if (string.IsNullOrWhiteSpace(offsetString))
            return TimeSpan.FromHours(-8);

        offsetString = offsetString.Trim();
        var isNegative = offsetString.StartsWith("-");
        var cleanOffset = offsetString.TrimStart('+', '-');

        if (TimeSpan.TryParse(cleanOffset, out var parsedOffset))
        {
            return isNegative ? -parsedOffset : parsedOffset;
        }

        return TimeSpan.FromHours(-8);
    }
}
