namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio configuration, bound from the "Twilio:" section. Values are supplied via user-secrets /
/// environment and are never hard-coded. The <see cref="AuthToken"/> is a secret: it is never logged,
/// never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164). Immediate messages are sent from it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used to schedule the delivery follow-up.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the MESSAGING API base address (send/read/reconcile). When set it is used
    /// verbatim for every messaging-API call instead of the provider default. It does NOT govern the
    /// Lookups API, which Twilio serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
