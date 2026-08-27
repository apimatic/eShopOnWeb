namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded or written to source files.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address (api.twilio.com by default).
    /// Governs only the messaging API; other Twilio capabilities keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
