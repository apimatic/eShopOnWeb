namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied by configuration
/// (environment / user-secrets) and are never hard-coded, so the same build can run against a
/// different Twilio account. The auth token is a secret: it is never logged or returned by an
/// endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164). Reconciliation counts only messages from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service used for scheduled follow-ups (scheduling requires a service).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the <em>messaging</em> API only — the one this
    /// integration sends, reads and reconciles messages through. When set it is used verbatim for
    /// every messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
