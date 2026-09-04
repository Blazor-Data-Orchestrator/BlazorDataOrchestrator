namespace BlazorOrchestrator.Web.Services.Community;

/// <summary>Administration-configurable settings for the Community Jobs Library integration.</summary>
public class CommunityOptions
{
    public const string SectionName = "Community";

    public string BaseUrl { get; set; } = "https://community.blazordata.net";

    public string ClientId { get; set; } = "bdo-desktop";

    public bool EnableCommunityPublish { get; set; }

    public bool EnableCommunityBrowse { get; set; }

    public bool AllowImport { get; set; } = true;

    public bool PreferDeviceCodeFlow { get; set; }

    public Guid InstallationId { get; set; }

    public string InstallationDisplayName { get; set; } = Environment.MachineName;

    /// <summary>Absolute redirect URI registered with the authorization server.</summary>
    public string RedirectUri { get; set; } = "https://localhost:7150/community/callback";

    public string AuthorizeEndpoint => $"{BaseUrl.TrimEnd('/')}/connect/authorize";
    public string TokenEndpoint => $"{BaseUrl.TrimEnd('/')}/connect/token";
    public string DeviceAuthorizationEndpoint => $"{BaseUrl.TrimEnd('/')}/connect/device";
    public string RevocationEndpoint => $"{BaseUrl.TrimEnd('/')}/connect/revocation";
    public string Issuer => BaseUrl.TrimEnd('/');
    public string McpEndpoint => $"{BaseUrl.TrimEnd('/')}/mcp";

    public static class Scopes
    {
        public const string Read = "cjl.read";
        public const string Publish = "cjl.publish";
        public const string Manage = "cjl.manage";
        public const string Social = "cjl.social";
        public const string Download = "cjl.download";
        public const string OfflineAccess = "offline_access";

        public static readonly string[] Default =
        [
            "openid", "profile", "email", OfflineAccess,
            Read, Publish, Manage, Social, Download
        ];
    }

    public static class SettingKeys
    {
        public const string BaseUrl = "Community:BaseUrl";
        public const string ClientId = "Community:ClientId";
        public const string EnablePublish = "Community:EnableCommunityPublish";
        public const string EnableBrowse = "Community:EnableCommunityBrowse";
        public const string AllowImport = "Community:AllowImport";
        public const string PreferDeviceCodeFlow = "Community:PreferDeviceCodeFlow";
        public const string InstallationId = "Community:InstallationId";
        public const string InstallationDisplayName = "Community:InstallationDisplayName";
        public const string RedirectUri = "Community:RedirectUri";
    }
}
