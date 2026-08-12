namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Binds the <c>Twilio:</c> configuration section. Values are supplied out-of-band (environment /
/// user-secrets) and never hard-coded, so the same build can run against a different account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (HTTP Basic username). Starts with <c>AC</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP Basic password). A secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164. Used as the message sender.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API only (send/read/reconcile). When
    /// set it is used verbatim for every messaging-API call. It does not govern the lookup API, which
    /// the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
