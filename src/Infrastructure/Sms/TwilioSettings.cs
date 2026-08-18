using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio messaging configuration, bound from the <c>Twilio</c> section. Values come from
/// configuration only (user-secrets / environment) — none are hard-coded — so the same build runs
/// against any account. The auth token is a secret and is never logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    [Required]
    public string AccountSid { get; set; } = string.Empty;

    [Required]
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number (E.164). Used as the sender for immediate messages and as the reconciliation filter.</summary>
    [Required]
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used for provider-side scheduled messages (the delivery follow-up).</summary>
    [Required]
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set it is used verbatim for every messaging
    /// call. It does NOT govern the lookups host, which the provider serves from a different address.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled for.</summary>
    [Range(1, 30)]
    public int FollowUpDelayDays { get; set; } = 3;

    /// <summary>Per-attempt timeout applied to each messaging call.</summary>
    [Range(1, 120)]
    public int PerAttemptTimeoutSeconds { get; set; } = 15;

    /// <summary>Whole-request budget for an order-event handler (which may make more than one call).</summary>
    [Range(1, 300)]
    public int RequestBudgetSeconds { get; set; } = 30;

    /// <summary>Whole-request budget for the reconciliation report, which pages the provider's ledger.</summary>
    [Range(1, 600)]
    public int ReconciliationBudgetSeconds { get; set; } = 120;
}
