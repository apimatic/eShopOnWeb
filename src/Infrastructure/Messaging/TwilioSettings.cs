namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values are supplied per-environment (via user-secrets
/// or environment configuration) and are never hard-coded, so the same build runs against any account.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for every
    /// messaging-API call (send/read/reconcile). It does NOT govern the Lookup API, which Twilio serves
    /// from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }

}
