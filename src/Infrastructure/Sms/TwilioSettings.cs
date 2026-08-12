namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration
/// section. Values are supplied through configuration / user-secrets and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (Basic-auth username). Bound from <c>Twilio:AccountSid</c>.</summary>
    public string? AccountSid { get; set; }

    /// <summary>Auth token (Basic-auth password). Secret — never logged or returned. Bound from <c>Twilio:AuthToken</c>.</summary>
    public string? AuthToken { get; set; }

    /// <summary>This application's own sending number in E.164. Bound from <c>Twilio:FromNumber</c>.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Messaging Service SID used when scheduling a future message. Bound from <c>Twilio:MessagingServiceSid</c>.</summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address (the host used to send, read, cancel,
    /// redact and reconcile messages). When set it is used verbatim for every messaging-API call.
    /// It does not govern the Lookup API, which is served from its own host. Bound from <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
