namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. Values must come from
/// environment / user-secrets — never from source files.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Programmable Messaging API host (send, fetch,
    /// update, list). Lookup continues to use lookups.twilio.com.
    /// </summary>
    public string? BaseUrl { get; set; }
}
