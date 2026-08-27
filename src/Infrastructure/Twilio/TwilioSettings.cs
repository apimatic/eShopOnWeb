namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded or written into the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address only (the API this integration
    /// sends, reads and reconciles messages through). Other Twilio capabilities (e.g. Lookup)
    /// are served from other hosts and are not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? "https://api.twilio.com"
        : BaseUrl.TrimEnd('/');

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountSid) || string.IsNullOrWhiteSpace(AuthToken) || string.IsNullOrWhiteSpace(FromNumber))
        {
            throw new System.InvalidOperationException(
                "Twilio settings are incomplete. Provide Twilio:AccountSid, Twilio:AuthToken and Twilio:FromNumber via user-secrets or environment variables.");
        }
    }
}
