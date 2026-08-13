namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (user-secrets / environment) and are never hard-coded, so the same build runs
/// against a different provider account by changing configuration alone.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    /// <summary>Account SID — the Basic-auth username for the messaging API.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the Basic-auth password. A secret: never logged, returned, or persisted in the repo.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Also the number reconciliation reports on.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID — required by the provider to schedule a message for later.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (send/read/reconcile). When set it is
    /// used verbatim for every messaging call in place of the provider default. It does not govern
    /// number lookup, which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
