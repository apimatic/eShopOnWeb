namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio settings, bound from the "Twilio" configuration section. Values are supplied by
/// configuration/user-secrets and are never hard-coded, so the same build can run against a
/// different Twilio account.
/// </summary>
public class TwilioOptions
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (send/read/reconcile). When set it is
    /// used verbatim for every messaging-API call. It does not govern the separate Lookup host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
