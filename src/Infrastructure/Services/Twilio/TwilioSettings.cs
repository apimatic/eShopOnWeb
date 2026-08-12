namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> section. Values are supplied via .NET
/// user-secrets / environment and are never hard-coded, so the same build runs against a different
/// Twilio account. The auth token is a secret: never logged, never returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number. Reconciliation counts only its messages.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service used for scheduled (future-dated) follow-up messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (send / read / reconcile). When set it is
    /// used verbatim for every messaging-API call instead of the provider default. It does NOT govern
    /// other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
