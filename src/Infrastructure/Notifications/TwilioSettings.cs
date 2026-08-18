using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are never hard-coded;
/// they are supplied through configuration (environment variables / user-secrets) so the same build
/// runs against a different Twilio account. The auth token is a secret and is never logged.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_SECTION = "Twilio";

    /// <summary>Account SID — used both as the Basic-auth username and as the account path argument.</summary>
    [Required]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the Basic-auth password. Secret: never logged, returned, or written to a source file.</summary>
    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Immediate messages are sent from it, and reconciliation counts only its traffic.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required to schedule the delivery-feedback follow-up.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging-API base address only. When set, it is used verbatim for
    /// every messaging call (send, read, list, redact, reconcile); the Lookup host is unaffected.
    /// </summary>
    public string? BaseUrl { get; set; }
}
