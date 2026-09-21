using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied by configuration
/// (environment variables / user-secrets) and never hard-coded. The auth token is a secret and is never
/// logged or returned by any endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number; also the only number reconciliation asks the provider about.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled (follow-up) sends.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the <b>messaging</b> API base address (Twilio's <c>api.twilio.com</c> node). When set,
    /// it is used verbatim for every messaging-API call (send/read/reconcile). It does not govern other Twilio
    /// hosts (e.g. Lookups), which keep their own base addresses.
    /// </summary>
    public string? BaseUrl { get; set; }
}
