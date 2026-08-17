namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the <c>Twilio:</c> configuration section. Values are supplied at runtime
/// (user-secrets / environment) and are never hard-coded, so the same build runs against a
/// different Twilio account.
/// </summary>
public class TwilioOptions
{
    public const string ConfigSection = "Twilio";

    /// <summary>Account SID (from <c>Twilio:AccountSid</c>). Also the HTTP Basic auth username.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (from <c>Twilio:AuthToken</c>). Secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own configured sending number (from <c>Twilio:FromNumber</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (from <c>Twilio:MessagingServiceSid</c>), used for scheduled sends.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address (from <c>Twilio:BaseUrl</c>). When set it
    /// is used verbatim for every messaging-API call. It does not govern the Lookups API, which Twilio
    /// serves from a different host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
