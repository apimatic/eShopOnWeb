namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied
/// via environment/user-secrets; none are committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (api.twilio.com by
    /// default). Governs only the messaging API; other Twilio capabilities
    /// (e.g. Lookups) keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
