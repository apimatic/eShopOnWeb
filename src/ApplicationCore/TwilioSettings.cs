namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied via
/// environment variables / user-secrets — never hard-code them.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Messaging API base address (replaces https://api.twilio.com).
    /// Does not apply to other Twilio hosts such as Lookups.
    /// </summary>
    public string? BaseUrl { get; set; }
}
