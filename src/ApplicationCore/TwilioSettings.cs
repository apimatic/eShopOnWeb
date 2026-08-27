namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive via
/// user-secrets or environment variables; none are hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host only. When set, it is used verbatim
    /// as the base address for every messaging-API call. Other capabilities (e.g. Lookup)
    /// keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
