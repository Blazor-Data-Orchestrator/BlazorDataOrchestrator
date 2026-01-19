namespace BlazorOrchestrator.Web.Services;

/// <summary>
/// Service for formatting DateTime values with the configured timezone offset
/// </summary>
public class TimeDisplayService
{
    private readonly AppSettingsService _appSettingsService;

    public TimeDisplayService(AppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService;
    }

    /// <summary>
    /// Converts a UTC DateTime to the configured display timezone
    /// </summary>
    public DateTime ConvertToDisplayTime(DateTime utcTime)
    {
        // If the time is not specified as UTC, assume it's already in the correct format
        // but still apply the offset for display purposes
        var offset = _appSettingsService.GetTimezoneOffset();
        return utcTime.Add(offset);
    }

    /// <summary>
    /// Formats a DateTime value using the configured timezone offset
    /// </summary>
    /// <param name="dateTime">The DateTime to format (assumed to be in UTC)</param>
    /// <param name="format">The format string (default: "g" for short date/time)</param>
    /// <returns>Formatted date/time string in the configured timezone</returns>
    public string FormatDateTime(DateTime dateTime, string format = "g")
    {
        var displayTime = ConvertToDisplayTime(dateTime);
        return displayTime.ToString(format);
    }

    /// <summary>
    /// Formats a nullable DateTime value using the configured timezone offset
    /// </summary>
    /// <param name="dateTime">The DateTime to format (assumed to be in UTC)</param>
    /// <param name="format">The format string (default: "g" for short date/time)</param>
    /// <param name="nullDisplay">Text to display when value is null (default: "-")</param>
    /// <returns>Formatted date/time string in the configured timezone, or nullDisplay if null</returns>
    public string FormatDateTime(DateTime? dateTime, string format = "g", string nullDisplay = "-")
    {
        if (!dateTime.HasValue)
            return nullDisplay;
        
        return FormatDateTime(dateTime.Value, format);
    }

    /// <summary>
    /// Gets the current timezone offset string for display purposes
    /// </summary>
    public string GetCurrentTimezoneDisplay()
    {
        return _appSettingsService.GetTimezoneOffsetString();
    }

    /// <summary>
    /// Gets the current time in the configured timezone
    /// </summary>
    public DateTime GetCurrentDisplayTime()
    {
        return ConvertToDisplayTime(DateTime.UtcNow);
    }

    /// <summary>
    /// Formats the current time in the configured timezone
    /// </summary>
    public string FormatCurrentTime(string format = "g")
    {
        return FormatDateTime(DateTime.UtcNow, format);
    }
}
