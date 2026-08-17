namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio configuration, bound from the "Twilio" configuration section. Values are supplied through
/// configuration/user-secrets only and are never hard-coded, so the same build runs against any account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written into a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164. Every message is sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a future message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set it is used verbatim as the
    /// base for every messaging-API call (send, read, cancel, redact, reconcile). It does NOT govern the
    /// Lookup API, which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
