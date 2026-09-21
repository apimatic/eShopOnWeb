namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied by configuration
/// (environment / user-secrets) and never hard-coded — the same build has to run against a different Twilio
/// account. The auth token is a secret: it is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio account SID (also the Basic-auth username and the Messages account path segment).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio auth token (Basic-auth password). Secret — never logged or serialized out.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164) used for immediate sends and reconciliation filtering.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required to schedule the delivery follow-up.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging (Messages) API host only. When set, it is used verbatim as the base
    /// address for every messaging-API call. Other Twilio hosts (e.g. Lookups) are not governed by this.
    /// </summary>
    public string? BaseUrl { get; set; }
}
