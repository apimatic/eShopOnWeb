using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Every value is supplied by
/// configuration (environment / user-secrets) — none is ever hard-coded, so the same build runs against a
/// different Twilio account. The required members are validated at startup so a missing credential stops the
/// app from booting rather than surfacing as a provider 401 on the first request.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Twilio Account SID — used both as the Basic-auth username and as the account path segment.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; set; } = null!;

    /// <summary>Twilio Auth Token (secret) — the Basic-auth password. Never logged, never returned, never written to a file.</summary>
    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; set; } = null!;

    /// <summary>The application's own sending number — the From on immediate sends and the number reconciliation counts against.</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; set; } = null!;

    /// <summary>The messaging service used for scheduled (follow-up) sends — scheduling is messaging-service only.</summary>
    [Required(AllowEmptyStrings = false)]
    public string MessagingServiceSid { get; set; } = null!;

    /// <summary>Optional override for the messaging API base URL. When set, it is used verbatim as the base
    /// address for every messaging-API call (send/read/reconcile); it does NOT govern the Lookup API.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Whole-call budget for a single provider request (bounds retries + backoff, not just one attempt).</summary>
    [Range(1, 120)]
    public int RequestTimeoutSeconds { get; set; } = 15;
}
