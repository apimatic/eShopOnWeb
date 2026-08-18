using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are never hard-coded — they
/// come from configuration (user-secrets / environment) so the same build can run against a different
/// Twilio account. The auth token is a secret: it is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_SECTION = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send/read/reconcile) instead of the provider default. It does not govern other
    /// hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
