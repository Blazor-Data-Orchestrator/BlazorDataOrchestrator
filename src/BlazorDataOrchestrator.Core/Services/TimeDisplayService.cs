using Microsoft.Extensions.Configuration;

namespace BlazorDataOrchestrator.Core.Services;

/// <summary>
/// Shared service for formatting DateTime values with DST-aware timezone conversion.
/// Reads timezone ID from Azure Table Storage via <see cref="SettingsService"/>,
/// with fallback to <see cref="IConfiguration"/> and a default of "America/Los_Angeles".
/// </summary>
public class TimeDisplayService
{
    private readonly SettingsService _settingsService;
    private readonly IConfiguration _configuration;
    private const string SettingKey = "TimezoneId";
    private const string DefaultTimezoneId = "America/Los_Angeles";

    // Cached TimeZoneInfo to avoid repeated Azure Table calls
    private TimeZoneInfo? _cachedTimeZone;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public TimeDisplayService(SettingsService settingsService, IConfiguration configuration)
    {
        _settingsService = settingsService;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets the configured TimeZoneInfo asynchronously, using cache when available.
    /// Fallback chain: Azure Table → IConfiguration → hardcoded default.
    /// </summary>
    public async Task<TimeZoneInfo> GetTimeZoneInfoAsync()
    {
        if (_cachedTimeZone != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedTimeZone;
        }

        string? timezoneId = null;

        // 1. Try Azure Table Storage
        try
        {
            timezoneId = await _settingsService.GetAsync(SettingKey);
        }
        catch
        {
            // Swallow — fall through to config
        }

        // 2. Fallback to IConfiguration
        if (string.IsNullOrEmpty(timezoneId))
        {
            timezoneId = _configuration[SettingKey];
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
    /// Gets the configured TimeZoneInfo synchronously from cache.
    /// If the cache is not populated yet, returns from IConfiguration or default.
    /// </summary>
    public TimeZoneInfo GetTimeZoneInfo()
    {
        if (_cachedTimeZone != null && DateTime.UtcNow < _cacheExpiry)
        {
            return _cachedTimeZone;
        }

        // Synchronous fallback: IConfiguration → default
        var timezoneId = _configuration[SettingKey] ?? DefaultTimezoneId;
        return ResolveTimeZone(timezoneId);
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
    /// Converts a UTC DateTime to display time using the configured timezone (DST-aware).
    /// </summary>
    public DateTime ConvertToDisplayTime(DateTime utcTime)
    {
        var tz = GetTimeZoneInfo();
        var utc = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    /// <summary>
    /// Formats a DateTime by applying the timezone conversion (DST-aware).
    /// </summary>
    public string FormatDateTime(DateTime dateTime, string format = "g")
    {
        var displayTime = ConvertToDisplayTime(dateTime);
        return displayTime.ToString(format);
    }

    /// <summary>
    /// Formats a nullable DateTime by applying the timezone conversion.
    /// </summary>
    public string FormatDateTime(DateTime? dateTime, string format = "g", string nullDisplay = "-")
    {
        if (!dateTime.HasValue)
            return nullDisplay;
        return FormatDateTime(dateTime.Value, format);
    }

    /// <summary>
    /// Gets the current display time (UTC converted to local timezone).
    /// </summary>
    public DateTime GetCurrentDisplayTime()
    {
        return ConvertToDisplayTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Formats the current time using the configured timezone.
    /// </summary>
    public string FormatCurrentTime(string format = "g")
    {
        return FormatDateTime(DateTime.UtcNow, format);
    }

    /// <summary>
    /// Gets the current timezone offset as a formatted string (e.g., "-07:00" during PDT).
    /// </summary>
    public string GetCurrentTimezoneDisplay()
    {
        var offset = GetTimezoneOffset();
        var sign = offset >= TimeSpan.Zero ? "+" : "-";
        var absOffset = offset.Duration();
        return $"{sign}{absOffset.Hours:D2}:{absOffset.Minutes:D2}";
    }

    /// <summary>
    /// Invalidates the cached timezone so the next read fetches from Azure Table Storage.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedTimeZone = null;
        _cacheExpiry = DateTime.MinValue;
    }

    private static TimeZoneInfo ResolveTimeZone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
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
}
