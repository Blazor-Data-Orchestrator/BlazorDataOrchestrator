using Microsoft.JSInterop;

namespace BlazorDataOrchestrator.JobCreatorTemplate.Services;

/// <summary>
/// Service for managing Copilot-related browser cookies via JS interop.
/// Provides persistence for user preferences like last selected model.
/// </summary>
public class CopilotCookieService
{
    private readonly IJSRuntime _jsRuntime;
    private const string LastUsedModelCookieName = "CopilotLastUsedModel";
    private const int CookieExpiryDays = 365;

    public CopilotCookieService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Retrieves the last used Copilot model from the browser cookie.
    /// </summary>
    /// <returns>The model name or null if not set.</returns>
    public async Task<string?> GetLastUsedModelAsync()
    {
        try
        {
            var value = await _jsRuntime.InvokeAsync<string?>("copilotCookies.get", LastUsedModelCookieName);
            
            // Decode the value if it was URL-encoded
            if (!string.IsNullOrEmpty(value))
            {
                return Uri.UnescapeDataString(value);
            }
            
            return value;
        }
        catch (JSException)
        {
            // JS interop failed (e.g., during prerendering)
            return null;
        }
        catch (InvalidOperationException)
        {
            // JS interop not available yet
            return null;
        }
    }

    /// <summary>
    /// Persists the selected Copilot model to a browser cookie.
    /// </summary>
    /// <param name="modelName">The model name to persist.</param>
    public async Task SetLastUsedModelAsync(string modelName)
    {
        var wasSaved = await _jsRuntime.InvokeAsync<bool>(
            "copilotCookies.set",
            LastUsedModelCookieName,
            modelName,
            CookieExpiryDays);

        if (!wasSaved)
        {
            throw new InvalidOperationException("The browser rejected the Copilot model cookie.");
        }
    }

    /// <summary>
    /// Removes the last used model cookie.
    /// </summary>
    public async Task ClearLastUsedModelAsync()
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("copilotCookies.remove", LastUsedModelCookieName);
        }
        catch (JSException)
        {
            // Ignore JS errors
        }
        catch (InvalidOperationException)
        {
            // JS interop not available
        }
    }
}
