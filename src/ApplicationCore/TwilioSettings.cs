using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed Twilio messaging configuration, bound from the <c>Twilio:</c> configuration section.
/// Values are supplied through configuration (environment / user-secrets) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own configured sending number (E.164). Reconciliation is scoped to it.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (MG...). Required by the provider to schedule a future-dated message.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the <em>messaging</em> API base URL only. When set it is used verbatim for every
    /// messaging-API call (send / read / reconcile). It does NOT govern the Lookup API, which lives on a
    /// different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
