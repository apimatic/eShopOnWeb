namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied by
/// configuration (environment / user-secrets) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (basic-auth username against the provider API).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (basic-auth password). A secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number all immediate messages are sent from and reconciled against.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging service used for scheduled messages (required by the provider for scheduling).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for
    /// every messaging-API call instead of the provider default. It does not govern other APIs
    /// (e.g. Lookups), which the provider serves from other hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
