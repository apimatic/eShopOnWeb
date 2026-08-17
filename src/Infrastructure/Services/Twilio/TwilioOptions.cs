namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Settings for the messaging provider, bound from the <c>Twilio:</c> configuration section.
/// Values are supplied through configuration/user-secrets and are never hard-coded, so the same
/// build runs against any account.
/// </summary>
public class TwilioOptions
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The account auth token. A secret: never logged, never returned, never written to a file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The configured sending number. Also the only "from" reconciliation counts against.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduling follow-ups.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API only. When set, it is used verbatim
    /// for every messaging-API call. When empty, the provider's default messaging host is used.
    /// It does not govern other provider hosts (for example number lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
