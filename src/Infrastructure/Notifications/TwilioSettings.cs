using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. All values are supplied
/// through configuration (environment variables / user-secrets) — none are hard-coded — so the same
/// build runs against a different Twilio account. The auth token is a secret and is never logged or
/// returned by any endpoint.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own configured sending number (also the reconciliation filter).</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service SID used for scheduled (future) messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
