namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are bound from configuration
/// (user-secrets / environment) and are never hard-coded; the same build runs against any Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>The account SID (basic-auth username). From <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The auth token (basic-auth password). Secret: never logged or returned. From <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number in E.164. From <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service SID used to schedule the follow-up. From <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set it is used verbatim for every
    /// messaging-API call (send / fetch / list / cancel / redact). Other Twilio hosts (e.g. Lookups)
    /// are not governed by this setting. From <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The default Twilio messaging API host, used when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The Twilio Lookups v2 host. Not governed by <see cref="BaseUrl"/>.</summary>
    public static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    public string EffectiveMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;
}
