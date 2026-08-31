namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied
/// via user-secrets/environment — never hard-coded.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used
    /// verbatim as the base address for every messaging-API call. It does not govern
    /// other Twilio capabilities (e.g. Lookup), which keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
