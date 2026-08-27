namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values arrive via
/// user-secrets / environment variables and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host only. When set, it is used verbatim
    /// as the base address for every messaging-API call instead of the provider default.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string EffectiveMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.twilio.com" : BaseUrl.TrimEnd('/');
}
