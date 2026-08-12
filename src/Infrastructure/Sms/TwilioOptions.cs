namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Settings for the Twilio integration, bound from the <c>Twilio:</c> configuration section. None of
/// these values are hard-coded; they are supplied through configuration / user-secrets so the same
/// build can run against a different Twilio account.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (Twilio:AccountSid). Also the HTTP Basic username.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (Twilio:AuthToken). Secret; the HTTP Basic password. Never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number in E.164 (Twilio:FromNumber).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (Twilio:MessagingServiceSid), required to schedule messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the messaging API (Twilio:BaseUrl). When set, it is
    /// used verbatim for every messaging-API call (send / read / reconcile). It does not govern the
    /// Lookup API, which Twilio serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
