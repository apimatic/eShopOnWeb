namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration section.
/// Values are supplied via configuration / user-secrets and are never hard-coded. The auth token is a
/// secret: it is never logged, never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number; reconciliation counts only messages sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled sends (scheduling is Messaging-Service-only).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send / read / list) instead of the provider default. It does not govern other
    /// Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
