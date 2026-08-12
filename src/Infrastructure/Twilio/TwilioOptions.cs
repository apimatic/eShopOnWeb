namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the "Twilio" configuration section.
/// Values are supplied by configuration/secrets and must never be hard-coded — the same build runs
/// against different Twilio accounts.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (basic-auth username).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (basic-auth password). Secret: never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own configured sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required for scheduling messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call. It does NOT govern the lookup API, which lives on a different host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The messaging base address to use: the override when set, otherwise Twilio's default.</summary>
    public string ResolvedMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.twilio.com" : BaseUrl!;
}
