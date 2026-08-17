using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied from
/// configuration (user-secrets / environment) and are never hard-coded. The credential members are
/// <see cref="RequiredAttribute"/> so a missing one fails startup rather than the first request.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    /// <summary>Twilio Account SID — Basic-auth username and the account path parameter for message operations.</summary>
    [Required]
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio Auth Token — the Basic-auth password. Secret: never logged or returned.</summary>
    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Used as the sender for immediate messages and as the reconciliation filter.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required as the sender for scheduled messages.</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set, it is used verbatim as the base address
    /// for every messaging-API call (send/read/reconcile/update/delete). It does not govern other Twilio
    /// hosts (e.g. Lookup). When unset, the provider default is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
