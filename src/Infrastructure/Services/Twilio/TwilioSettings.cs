namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Provider credentials and configuration, bound from the "Twilio" configuration section. Values are
/// supplied via user-secrets / environment variables and never hard-coded, so the same build can run
/// against a different provider account.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_SECTION = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The account auth token. A secret — never logged, returned, or written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number (E.164). All messages are sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service used for scheduled sends (scheduling requires one).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (the host messages are sent, read and
    /// reconciled through). When set it is used verbatim for every messaging-API call; when empty the
    /// provider default is used. Does not govern other provider hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
