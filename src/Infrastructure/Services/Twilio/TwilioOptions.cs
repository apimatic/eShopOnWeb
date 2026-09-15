namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed settings bound from the <c>Twilio:</c> configuration section. None of these
/// values are hard-coded - the same build runs against a different Twilio account by changing config.
/// The auth token is a secret: it is never logged, never returned by an endpoint, and never written
/// into a source file.
/// </summary>
public class TwilioOptions
{
    public const string CONFIG_NAME = "Twilio";

    /// <summary>Account SID - the Basic-auth username for the API (from <c>TWILIO_ACCOUNT_SID</c>).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Auth token - the Basic-auth password (from <c>TWILIO_AUTH_TOKEN</c>). Secret.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number in E.164 (from <c>TWILIO_FROM_NUMBER</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>The Messaging Service SID used for scheduled messages (from <c>TWILIO_MESSAGING_SERVICE_SID</c>).</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim as the base
    /// for every messaging-API call (send, read, reconcile). It does not govern other provider hosts
    /// (such as lookup). When unset, the provider's default messaging host is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
