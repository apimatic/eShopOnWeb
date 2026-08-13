namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio configuration, bound from the <c>Twilio:</c> configuration section. Values are never
/// hard-coded — they come from configuration (user-secrets / environment) so the same build can run
/// against a different Twilio account.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Account SID (HTTP basic auth username). From <c>Twilio:AccountSid</c>.</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token (HTTP basic auth password). Secret: never logged or returned. From <c>Twilio:AuthToken</c>.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, in E.164. From <c>Twilio:FromNumber</c>.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule a future message. From <c>Twilio:MessagingServiceSid</c>.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the base address of the <b>messaging</b> API only. When set, it is
    /// used verbatim for every messaging-API call instead of the provider default. From <c>Twilio:BaseUrl</c>.</summary>
    public string? BaseUrl { get; set; }
}
