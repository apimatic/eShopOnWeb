namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>
/// Strongly-typed settings for the Twilio integration, bound from the <c>Twilio:</c>
/// configuration section. Values are supplied via configuration / user-secrets and are
/// never hard-coded. The <see cref="AuthToken"/> is a secret and must never be logged or
/// returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    /// <summary>Configuration section name.</summary>
    public const string CONFIG_SECTION = "Twilio";

    /// <summary>The default host for the Twilio "messaging" API (the 2010-04-01 REST API).</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The host for the Twilio Lookups API. This is a distinct capability served from
    /// its own host and is deliberately NOT governed by <see cref="BaseUrl"/>.</summary>
    public static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    /// <summary>Account SID (Basic-auth username). Bound from <c>Twilio:AccountSid</c>.</summary>
    public string? AccountSid { get; set; }

    /// <summary>Auth token (Basic-auth password). Bound from <c>Twilio:AuthToken</c>. Secret.</summary>
    public string? AuthToken { get; set; }

    /// <summary>The configured sending number in E.164. Bound from <c>Twilio:FromNumber</c>.
    /// Also the number reconciliation is scoped to.</summary>
    public string? FromNumber { get; set; }

    /// <summary>Messaging Service SID used to schedule follow-up messages. Bound from
    /// <c>Twilio:MessagingServiceSid</c>. Scheduling a message requires a Messaging Service.</summary>
    public string? MessagingServiceSid { get; set; }

    /// <summary>Optional override for the messaging-API base address. Bound from
    /// <c>Twilio:BaseUrl</c>. When set, it is used verbatim for every messaging-API call.
    /// It does not affect the Lookups API.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective base address for the messaging API.</summary>
    public string EffectiveMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.TrimEnd('/');

    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(AccountSid) && !string.IsNullOrWhiteSpace(AuthToken);
}
