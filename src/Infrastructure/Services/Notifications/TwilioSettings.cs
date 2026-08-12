namespace Microsoft.eShopWeb.Infrastructure.Services.Notifications;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values come from configuration
/// (user-secrets in local runs) and are never hard-coded — the same build must run against a
/// different Twilio account. The auth token is a secret: it is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID — the Basic-auth username for the messaging API.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the Basic-auth password. Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number. Immediate messages are sent from it and reconciliation counts only its traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled (future-dated) messages, which Twilio only supports via a Messaging Service.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call (send/fetch/list/update/delete); it does not affect other Twilio hosts such
    /// as Lookup. When unset, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
