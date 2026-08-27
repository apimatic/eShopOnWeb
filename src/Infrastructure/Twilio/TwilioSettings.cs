namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section.
/// Values are supplied via user-secrets or environment variables — never from source control.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used
    /// verbatim for every messaging-API call instead of https://api.twilio.com.
    /// Does not govern other Twilio capabilities (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
