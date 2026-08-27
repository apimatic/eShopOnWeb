namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets
/// or environment variables; none are hard-coded or logged.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used
    /// verbatim as the base address for every messaging-API call. Other Twilio
    /// capabilities (e.g. Lookup) are served from other hosts and are not governed by this.
    /// </summary>
    public string? BaseUrl { get; set; }
}
