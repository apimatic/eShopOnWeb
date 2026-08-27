namespace Microsoft.eShopWeb.ApplicationCore.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied
/// through user-secrets/environment variables and must never be committed.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API (api.twilio.com) base address only.
    /// When set it is used verbatim for every messaging-API call.
    /// </summary>
    public string? BaseUrl { get; set; }
}
