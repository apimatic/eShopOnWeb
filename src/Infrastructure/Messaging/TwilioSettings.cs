namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the <c>Twilio</c> configuration section. No value is ever hard-coded: the same build must run
/// against a different Twilio account by changing configuration/secrets alone.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Twilio Account SID (Basic auth username). Key: <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Twilio Auth Token (Basic auth password). Secret — never logged or returned. Key: <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number in E.164. Key: <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (required for scheduled sends). Key: <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, used verbatim for every messaging-API
    /// call in place of the provider default. Does not govern other Twilio hosts (e.g. Lookup).
    /// Key: <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The provider's default messaging host, used when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The Lookup host — a different capability, not governed by <see cref="BaseUrl"/>.</summary>
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    /// <summary>The effective messaging base address: the override if present, otherwise the provider default.</summary>
    public string ResolveMessagingBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.TrimEnd('/');
}
