namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed Twilio configuration, bound from the "Twilio" configuration section. Values are supplied
/// through configuration/user-secrets and are never hard-coded, so the same build runs against any account.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret. Never logged, never returned by an endpoint, never written to a source file.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number, in E.164. Used as the sender for immediate messages
    /// and as the reconciliation sender filter.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider for scheduled (future-dated) messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send, read, reconcile). It does NOT govern the Lookup API, which lives on its
    /// own host. When unset, the provider default (https://api.twilio.com) is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
