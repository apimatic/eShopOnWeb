namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Twilio configuration, bound from the "Twilio" section. Values are supplied via configuration
/// (user-secrets / environment), never hard-coded, so the same build runs against a different account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number; also the sender reconciliation filters on.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by Twilio to schedule a message for future delivery.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set it is used verbatim for every
    /// messaging-API call (send, read, reconcile, redact, cancel, schedule). It does not govern other Twilio
    /// hosts such as Lookups. When unset, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
