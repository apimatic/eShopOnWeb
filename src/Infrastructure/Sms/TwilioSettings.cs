namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied
/// from configuration/user-secrets only and are never hard-coded, so the same build runs
/// against any account. The auth token is a secret and is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    /// <summary>Account SID (from <c>TWILIO_ACCOUNT_SID</c>). Used as the Basic-auth username and path parameter.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (from <c>TWILIO_AUTH_TOKEN</c>). Secret — Basic-auth password only.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number in E.164 (from <c>TWILIO_FROM_NUMBER</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (from <c>TWILIO_MESSAGING_SERVICE_SID</c>). Required for scheduled sends.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for
    /// every messaging-API call (send/read/redact/list). It does NOT govern the Lookup API,
    /// which the provider serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
