namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied from
/// configuration (environment / user-secrets) and are never hard-coded. The <see cref="AuthToken"/>
/// is a secret: it is never logged, never returned by an endpoint, and never written to a source file.
/// </summary>
public class TwilioSettings
{
    /// <summary>The account SID — basic-auth username. (Twilio:AccountSid)</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>The auth token — basic-auth password. Secret. (Twilio:AuthToken)</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The sending number immediate messages go out from and reconciliation counts. (Twilio:FromNumber)</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The messaging service used to queue scheduled follow-up messages. (Twilio:MessagingServiceSid)</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim for every
    /// messaging-API call (send/read/reconcile). It does NOT govern the phone-number Lookup host, which
    /// Twilio serves from a different host. (Twilio:BaseUrl)
    /// </summary>
    public string? BaseUrl { get; set; }
}
