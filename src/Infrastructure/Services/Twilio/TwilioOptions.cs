namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via
/// user-secrets/environment and must never be committed to the repo.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set it is
    /// used verbatim for every messaging-API call instead of https://api.twilio.com.
    /// Other Twilio capabilities (e.g. Lookup) are served from their own hosts
    /// and are not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
