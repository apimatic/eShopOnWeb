namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio configuration, bound from the "Twilio" section. Values are supplied via user-secrets /
/// environment; none are hard-coded. The auth token is a secret and is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>eShop's own configured sending number (E.164). Reconciliation counts only this number's traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for every send, so each message's sender is <see cref="FromNumber"/> and scheduling is available.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call instead of Twilio's default. It does NOT govern the Lookup API, which lives on a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
