namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets
/// or environment variables; none are hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API (api.twilio.com) base address.
    /// Does not govern other Twilio hosts (e.g. lookups.twilio.com).
    /// </summary>
    public string? BaseUrl { get; set; }
}
