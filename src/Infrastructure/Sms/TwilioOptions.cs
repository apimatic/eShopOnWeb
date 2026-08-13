namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings for the Twilio messaging integration, bound from the <c>Twilio:</c> configuration
/// section. Values are supplied through configuration/user-secrets and are never hard-coded.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>The provider's default messaging host, used when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>
    /// The Lookup host. Twilio serves Lookup from a different host than the messaging API, and the
    /// <see cref="BaseUrl"/> override governs only the messaging API — so this is fixed here.
    /// </summary>
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    /// <summary>Account SID (Basic-auth username). From <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (Basic-auth password). Secret: never logged, returned, or written to a file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164. From <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled sends. From <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send, read, redact, reconcile) instead of the provider default. Does not
    /// affect Lookup, which lives on another host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective base address for messaging-API calls.</summary>
    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.TrimEnd('/');
}
