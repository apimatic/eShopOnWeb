namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values are supplied via
/// environment variables / user-secrets — never from source.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Programmable Messaging API root
    /// (default <c>https://api.twilio.com/2010-04-01</c>). Lookup uses a different host
    /// and is not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
