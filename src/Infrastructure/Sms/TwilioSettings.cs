namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Binds the <c>Twilio:</c> configuration section. Values are supplied through configuration
/// (user-secrets / environment) and are never hard-coded, so the same build runs against any account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Account SID — the HTTP Basic username and the account path segment.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the HTTP Basic password. A secret: never logged, returned, or written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The configured sending number (E.164). Used as the sender and as the reconciliation filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID. Required by the provider to schedule a message.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call (send, read, reconcile). It does not govern other hosts such as lookup.
    /// </summary>
    public string? BaseUrl { get; set; }
}
