namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Provider settings bound from the <c>Twilio:</c> configuration section. No value is ever hard-coded;
/// the same build has to run against a different account. The auth token is a secret and is never
/// logged, returned by an endpoint, or written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    /// <summary>Account SID (begins <c>AC</c>). Also a required path parameter on the messaging API.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the password half of HTTP Basic. Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Reconciliation counts only this sender's traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (begins <c>MG</c>). Required by the provider to schedule a future send.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API this integration sends, reads and
    /// reconciles messages through. When set it is used verbatim for every messaging-API call. It does not
    /// govern other provider hosts (such as lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }


    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The base address to use for messaging-API calls: the override if set, otherwise the default.</summary>
    public string ResolveMessagingBaseUrl() =>
        (string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!).TrimEnd('/');
}
