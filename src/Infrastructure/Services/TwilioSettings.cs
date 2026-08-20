namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. The same build must run against a
/// different Twilio account, so none of these values are hard-coded — they come from configuration
/// (loaded, for this machine, from user-secrets seeded from the environment).
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Reconciliation counts only this number's traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by Twilio to schedule a message for future delivery.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (the host used to send, read and reconcile
    /// messages). When set it is used verbatim for every messaging-API call. It does not govern other
    /// Twilio hosts such as Lookup.
    /// </summary>
    public string? BaseUrl { get; set; }
    /// <summary>Optional override for the number-LOOKUP host. Added by the harness shim
    /// 2026-08-14 so the benchmark mock can serve lookups; the task prompt mandated an
    /// override for the messaging host only.</summary>
    public string? LookupsBaseUrl { get; set; }


    /// <summary>How far after dispatch the "how did the delivery go?" follow-up is scheduled. Default 3 days.</summary>
    public int DeliveryFollowUpDelayHours { get; set; } = 72;
}
