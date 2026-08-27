namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets/environment;
/// none of them may be written into source files.
/// </summary>
public class TwilioSettings
{
    public const string ConfigName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (the API this integration sends,
    /// reads and reconciles messages through). When set, used verbatim for every messaging-API
    /// call. Does not govern other Twilio capabilities (e.g. Lookup), which live on other hosts.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string EffectiveBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? "https://api.twilio.com"
        : BaseUrl.TrimEnd('/');
}
