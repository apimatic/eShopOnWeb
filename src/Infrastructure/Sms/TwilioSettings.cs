namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Binds the <c>Twilio:</c> configuration section. No value is ever hard-coded; the same build runs
/// against a different Twilio account by changing configuration alone. The auth token is a secret and
/// is never logged, returned by an endpoint, or written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Reconciliation asks the provider only for this number's messages.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service used for scheduled sends (the delivery follow-up).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API this integration sends, reads and
    /// reconciles messages through. When set it is used verbatim for every messaging-API call. When
    /// empty the provider default is used. It does not govern other provider hosts (e.g. lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
