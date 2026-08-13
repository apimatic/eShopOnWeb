namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied at runtime
/// (user-secrets / environment) and never hard-coded, so the same build runs against any account.
/// The auth token is a secret: it is only ever used to build the Authorization header and is never
/// logged, returned by an endpoint, or written to a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (HTTP Basic username), from <c>TWILIO_ACCOUNT_SID</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP Basic password), from <c>TWILIO_AUTH_TOKEN</c>. Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number in E.164, from <c>TWILIO_FROM_NUMBER</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID used for scheduled sends, from <c>TWILIO_MESSAGING_SERVICE_SID</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (the host this integration sends, reads
    /// and reconciles messages through). When set it is used verbatim for every messaging-API call.
    /// It does not govern the Lookup API, which is served from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
