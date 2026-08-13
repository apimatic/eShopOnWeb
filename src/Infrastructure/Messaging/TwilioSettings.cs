namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> configuration section. None of these values are
/// hard-coded; the same build runs against a different Twilio account by supplying different values.
/// The auth token is a secret — it is never logged, returned by an endpoint, or written to a file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim as the
    /// base address for every messaging-API call (send, read, reconcile). It does not govern other
    /// provider hosts such as number lookup. When unset, the provider's default host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
