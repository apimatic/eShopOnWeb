namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (user-secrets / environment) — none are hard-coded — so the same build can run
/// against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    /// <summary>Account SID — the Basic-auth username for the messaging and lookup APIs.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token — the Basic-auth password. A secret: never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, used as <c>From</c> and as the reconciliation filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required to schedule the delivery-feedback follow-up.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send, read, reconcile). It does not govern the Lookup API, which is served
    /// from its own host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
