namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration section.
/// None of these values are hard-coded; the same build runs against a different Twilio account by
/// supplying a different configuration. The <see cref="AuthToken"/> is a secret and is never logged,
/// returned by an endpoint, or written into a source file.
/// </summary>
public class TwilioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Twilio";

    /// <summary>Account SID (from TWILIO_ACCOUNT_SID). Used as the Basic-auth username and in the API path.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (from TWILIO_AUTH_TOKEN). Secret; used as the Basic-auth password.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending phone number in E.164 (from TWILIO_FROM_NUMBER). Immediate messages are sent from it,
    /// and reconciliation counts only messages sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (from TWILIO_MESSAGING_SERVICE_SID). Required to schedule the follow-up message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API only. When set, it is used verbatim for every
    /// messaging-API call instead of the provider default. It does not govern the Lookup API, which Twilio serves
    /// from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
