namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio configuration, bound from the "Twilio" section. Values are supplied out-of-band
/// (environment / user-secrets) and are never committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_SECTION = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a message for later.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call. It does not govern the Lookup API, which the provider serves from another host.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }

}
