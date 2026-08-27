namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets /
/// environment variables and are never committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host (api.twilio.com) only. Other
    /// Twilio capabilities (e.g. Lookups) are served from their own hosts and are
    /// not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
