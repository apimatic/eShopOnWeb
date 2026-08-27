namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied via
/// user-secrets/environment variables and must never be committed to source.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string? AccountSid { get; set; }
    public string? AuthToken { get; set; }
    public string? FromNumber { get; set; }
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address (api.twilio.com by default).
    /// Does not govern other Twilio capabilities such as Lookup.
    /// </summary>
    public string? BaseUrl { get; set; }
}
