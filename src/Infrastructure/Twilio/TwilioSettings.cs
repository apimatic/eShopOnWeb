namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Secrets (AuthToken) are
/// supplied via user-secrets or environment variables, never from source-controlled files.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address (api.twilio.com by default).
    /// Governs only the messaging API; other Twilio capabilities (e.g. Lookup) keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
