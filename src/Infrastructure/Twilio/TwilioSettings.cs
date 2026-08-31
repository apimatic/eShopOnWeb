namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets
/// or environment-specific configuration; none are committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for scheduling messages for future delivery.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (the API this
    /// integration sends, reads and reconciles messages through). When set it is
    /// used verbatim for every messaging-API call. Other Twilio capabilities
    /// (e.g. Lookup) are served from other hosts and are not governed by this.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? "https://api.twilio.com"
        : BaseUrl!.TrimEnd('/');

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) &&
        !string.IsNullOrWhiteSpace(AuthToken) &&
        !string.IsNullOrWhiteSpace(FromNumber);
}
