using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied by configuration
/// (environment variables / user-secrets) and are never hard-coded. The auth token is a secret: it is never
/// logged, never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number. Immediate messages are sent from it and the
    /// reconciliation report is scoped to it.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used for scheduled (follow-up) messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging-API base address. When set, it is used verbatim for every
    /// messaging-API call (send / read / reconcile). It does NOT affect the phone-number Lookup call, which
    /// the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
