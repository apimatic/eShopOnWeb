namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Bound from the <c>Twilio:</c> configuration section. No value is ever hard-coded; the same build
/// runs against any account by supplying a different configuration/secret set.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (HTTP Basic username). <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP Basic password) — a secret; never logged or returned. <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number in E.164. <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled sends. <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim for
    /// every messaging-API call (send, fetch, schedule, cancel, redact, list). Other capabilities such
    /// as Lookup are served from other hosts and are not governed by this. <c>Twilio:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }

}
