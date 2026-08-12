namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration section.
/// Values are supplied at runtime (user-secrets / environment) and are never hard-coded.
/// </summary>
public class TwilioOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Twilio";

    /// <summary>Account SID (HTTP Basic username). From <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP Basic password). Secret — never logged or returned. From <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number in E.164. From <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled sends. From <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, used verbatim for every
    /// messaging-API call (send, fetch, update, list). Does not govern the lookup host.
    /// From <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
