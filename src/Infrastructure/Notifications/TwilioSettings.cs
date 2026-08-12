namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio credentials and endpoints, bound from the <c>Twilio:</c> configuration section. Values are
/// supplied via .NET user-secrets / environment and are never written into the repository.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Twilio Account SID (public identifier). Bound from <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio auth token (secret). Bound from <c>Twilio:AuthToken</c>. Never logged/returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number in E.164. Bound from <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled messages. Bound from <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call (send/read/reconcile). It does NOT govern the Lookups API. Bound from
    /// <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Default messaging API host when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The Lookups API host. Served from a different host than the messaging API.</summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";
}
