namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied by
/// configuration / user-secrets / environment — never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Account SID (from <c>Twilio:AccountSid</c>).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (from <c>Twilio:AuthToken</c>). Secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number in E.164 (from <c>Twilio:FromNumber</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled messages (from <c>Twilio:MessagingServiceSid</c>).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (from <c>Twilio:BaseUrl</c>). When set
    /// it is used verbatim for every messaging-API call. It does NOT govern the Lookup API, which
    /// Twilio serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }

}
