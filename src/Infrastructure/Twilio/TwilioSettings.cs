namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via
/// user-secrets/environment variables; none are hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host only (the spec default is
    /// https://api.twilio.com). Does not govern other Twilio capabilities.
    /// </summary>
    public string? BaseUrl { get; set; }
}
