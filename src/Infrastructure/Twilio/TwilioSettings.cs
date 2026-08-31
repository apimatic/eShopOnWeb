namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values are supplied via user-secrets or
/// environment variables — never from files committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only (the api.twilio.com host).
    /// Other Twilio capabilities (e.g. Lookup) are served from their own hosts and are
    /// not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        !string.IsNullOrWhiteSpace(FromNumber);
}
