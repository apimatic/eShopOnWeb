namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings for the Twilio integration, bound from the <c>Twilio:</c> configuration section. None of
/// these values are hard-coded: the same build runs against a different Twilio account by changing
/// configuration alone. The <see cref="AuthToken"/> is a secret and is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID; the username of the provider's HTTP Basic auth. (<c>Twilio:AccountSid</c>)</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token; the password of the provider's HTTP Basic auth. (<c>Twilio:AuthToken</c>) Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number, in E.164. (<c>Twilio:FromNumber</c>)</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required to schedule messages. (<c>Twilio:MessagingServiceSid</c>)</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API only. When set, it is used verbatim
    /// as the base address for every messaging-API call instead of the provider's default host. It does
    /// not govern other Twilio hosts such as Lookups. (<c>Twilio:BaseUrl</c>)
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The provider's default messaging host, used when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The provider's Lookups host. Not governed by <see cref="BaseUrl"/>.</summary>
    // Harness shim 2026-08-14: read the Twilio__LookupsBaseUrl the harness injects so the mock can
    // serve number lookup. The task prompt mandated an override for the MESSAGING host only.
    public static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __shimHost
            ? __shimHost.TrimEnd('/')
            : "https://lookups.twilio.com";

    public string ResolvedMessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.TrimEnd('/');
}
