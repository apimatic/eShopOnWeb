namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the <c>Twilio:</c> section. Credential values are
/// never hard-coded — they come from user-secrets / environment configuration. The four credential
/// properties are validated non-blank at startup (see <see cref="TwilioMessagingServiceCollectionExtensions"/>).
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID — Basic-auth username and the account path segment on every message call.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — Basic-auth password. Secret: never logged, never returned, never in a file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The configured sending number; immediate notices are sent from it and reconciliation counts only it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required for scheduled (follow-up) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set, it is used verbatim for every
    /// messaging-API call (send/read/reconcile). It does NOT govern number-lookup calls.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Total per-call budget (seconds) enforced via a linked CancellationToken.</summary>
    public int CallTimeoutSeconds { get; set; } = 30;

    /// <summary>How many days after dispatch the "how did delivery go?" follow-up is scheduled for.</summary>
    public int FollowUpDelayDays { get; set; } = 3;

    /// <summary>Page cap for the reconciliation walk; if hit, the report is flagged truncated.</summary>
    public int MaxReconciliationPages { get; set; } = 50;

    /// <summary>Page size requested from the provider's message list.</summary>
    public int ListPageSize { get; set; } = 1000;
}
