namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Twilio messaging configuration, bound from the "Twilio" section. Values are supplied via
/// configuration/user-secrets and are never hard-coded. The auth token is a secret and must
/// never be logged or returned by an endpoint.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164). Reconciliation is scoped to it.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (MG...). Required by Twilio to schedule future messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging REST API base address. When set it is used verbatim
    /// for every messaging-API call. It does NOT govern the Lookup API (a different Twilio host).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How far in the future the "how did delivery go?" follow-up is scheduled.</summary>
    public int FollowUpDelayDays { get; set; } = 3;
}
