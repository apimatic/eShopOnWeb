namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings for the Twilio integration, bound from the <c>Twilio:</c> configuration section. Values are
/// never hard-coded — the same build has to run against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (an identifier, used in the messaging API path).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — a secret. Used only to build the Basic auth header; never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider for scheduled sends.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call instead of the provider's default. Does not govern other Twilio hosts (e.g.
    /// Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
