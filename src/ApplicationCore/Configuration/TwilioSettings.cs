namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration
/// section. Values are never hard-coded — they come from configuration / user-secrets so the
/// same build can run against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Account SID (Basic-auth username for the provider).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (Basic-auth password). Secret: never logged, returned, or written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number (E.164). Reconciliation counts only messages sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled (send-later) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL (the API through which this integration
    /// sends, reads and reconciles messages). When set, it is used verbatim for every messaging-API
    /// call instead of the provider default. It does NOT govern other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
