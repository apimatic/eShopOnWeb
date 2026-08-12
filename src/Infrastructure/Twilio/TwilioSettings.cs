namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed messaging-provider settings, bound from the <c>Twilio:</c> configuration section.
/// Values are never hard-coded — the same build runs against a different Twilio account by changing config.
/// The auth token is a secret and is never logged or returned by any endpoint.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own E.164 sending number (also the reconciliation sender filter).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service (MG…) used to send and schedule messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, every messaging-API call
    /// uses it verbatim; the lookup API keeps its own separate host regardless.
    /// </summary>
    public string? BaseUrl { get; set; }
}
