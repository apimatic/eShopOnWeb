namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the Twilio: configuration section. Secret values must come from
/// environment variables or user-secrets, never from source files.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the Programmable Messaging API host only
    /// (send, fetch, list, update). Lookup uses lookups.twilio.com.
    /// </summary>
    public string? BaseUrl { get; set; }
}
