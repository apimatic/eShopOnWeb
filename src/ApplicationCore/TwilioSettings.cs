namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied
/// via user-secrets or environment-specific configuration; none are committed.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host (the API used to send, read
    /// and reconcile messages). When set, it is used verbatim as the base address
    /// for every messaging-API call. Other Twilio capabilities (e.g. Lookup) are
    /// served from their own hosts and are not governed by this setting.
    /// </summary>
    public string? BaseUrl { get; set; }
}
