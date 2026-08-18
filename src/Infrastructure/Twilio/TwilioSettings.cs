using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. No value is ever hard-coded — the same build runs
/// against a different Twilio account by changing configuration only. The auth token is a secret and is
/// never logged, returned, or written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number (E.164). Reconciliation counts only its traffic.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required to queue a scheduled (future-dated) message with the provider.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the MESSAGING API base address only. When set, it is used verbatim for every
    /// messaging-API call (send, read, reconcile). It does not govern the lookup API, which the provider
    /// serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
