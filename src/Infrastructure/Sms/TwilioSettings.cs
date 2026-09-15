namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Values are supplied by
/// configuration / user-secrets and are never hard-coded, so the same build can run against a
/// different Twilio account. The auth token is a secret: it is never logged, never returned by an
/// endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call. Twilio serves other capabilities (e.g. Lookup) from other hosts, which
    /// this setting does not govern.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The messaging-API base address: the override when set, otherwise Twilio's default.</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.twilio.com" : BaseUrl!.TrimEnd('/');

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
