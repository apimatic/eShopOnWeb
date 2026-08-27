namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables — never from files committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string ConfigName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API (api.twilio.com) only. When set, it is
    /// used verbatim as the base address for every messaging-API call. Other Twilio
    /// capabilities (e.g. Lookups) are served from other hosts and are not governed
    /// by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
