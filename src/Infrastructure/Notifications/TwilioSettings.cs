namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are never hard-coded — they come from
/// user-secrets / environment configuration so the same build runs against any Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID — used as the Basic-auth username and the <c>accountSid</c> path parameter.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the Basic-auth password. Secret: never logged, returned or written to a file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number immediate messages are sent from and reconciliation filters on.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used to queue scheduled follow-ups (scheduling is service-only).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (the api.twilio.com host through which messages are
    /// sent, read and reconciled). Null/blank ⇒ the provider default. Does not govern the lookups host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
