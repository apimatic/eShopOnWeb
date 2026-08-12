namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio credentials and endpoints, bound from the <c>Twilio:</c> configuration
/// section. Values are supplied via configuration/user-secrets and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (also the HTTP Basic username). From <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (the HTTP Basic password). Secret. From <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number in E.164. From <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required for scheduled sends. From <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim
    /// for every messaging-API call in place of the provider default. Does not govern the
    /// Lookup API, which lives on a different host. From <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
