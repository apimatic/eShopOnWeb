namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (user-secrets / environment) and are never hard-coded, so the same build can
/// run against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID; the username of the provider's HTTP Basic auth.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token; the password of the provider's HTTP Basic auth. A secret — never logged.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, in E.164 form.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a message for later.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for
    /// every messaging-API call in place of the provider's default host. It does not govern the
    /// lookup API, which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
