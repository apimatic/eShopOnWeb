using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. Every required part is
/// validated at startup (see <see cref="TwilioServiceCollectionExtensions"/>) so a missing or blank
/// credential stops the host from starting rather than surfacing as a 401 on the first message.
/// Values are supplied by configuration/user-secrets and are never hard-coded.
/// </summary>
public sealed class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio Account SID — the account path segment on every messaging-API call, and the Basic-auth username.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio auth token — the Basic-auth password. A secret: never logged, returned, or written to a source file.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Immediate messages are sent from it; reconciliation counts only it.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service SID used to schedule the delivery follow-up (scheduling requires a Messaging Service).</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API's base address. When set, it is used verbatim for every
    /// messaging-API call (send/read/reconcile). It does not govern other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
