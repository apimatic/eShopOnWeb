using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (user-secrets / environment) and never hard-coded. The auth token is a secret and
/// is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    [Required]
    public string FromNumber { get; set; } = string.Empty;

    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim as the base
    /// address for every messaging-API call (send, read, update, list). It does not govern other Twilio
    /// hosts (e.g. Lookup). When unset, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
