namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Values are supplied at runtime
/// (env-fed user-secrets) and never hard-coded, so the same build runs against any Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    /// <summary>Account SID (username for HTTP Basic auth).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (password for HTTP Basic auth). Secret: never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, in E.164. Used for immediate sends and as the reconciliation filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID. Required for scheduled sends (Twilio does not allow scheduling with a bare From).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (send/read/reconcile). When set it is used
    /// verbatim for every messaging call in place of Twilio's default. It does not govern other Twilio
    /// hosts (e.g. Lookup), which Twilio serves elsewhere.
    /// </summary>
    public string? BaseUrl { get; set; }
}
