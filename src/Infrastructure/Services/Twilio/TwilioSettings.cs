namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio configuration, bound from the "Twilio" configuration section. Values are supplied through
/// configuration (user-secrets / environment) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (HTTP Basic username).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP Basic password). A secret: never logged, never returned, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The configured sending number. Immediate messages and reconciliation use this number.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required to schedule the delayed follow-up.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only (send/read/reconcile). When set it is
    /// used verbatim for every messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
