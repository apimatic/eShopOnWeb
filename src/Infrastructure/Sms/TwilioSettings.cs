using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Strongly-typed <c>Twilio:</c> configuration. Bound and validated at startup so a missing
/// credential stops the app from booting rather than surfacing as a provider 401 on the first
/// message. Values come from configuration (user-secrets / environment) — never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number. Immediate messages are sent from it and reconciliation counts only its traffic.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used for scheduled (future-dated) messages.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send, read, schedule, cancel, redact, list). It does not affect other
    /// Twilio hosts (e.g. phone-number Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
