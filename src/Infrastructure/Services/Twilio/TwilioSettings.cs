namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables; none are committed to the repository.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API host (api.twilio.com). When set, it is
    /// used verbatim as the base address for every messaging-API call. It does not
    /// govern other Twilio hosts (e.g. lookups.twilio.com).
    /// </summary>
    public string? BaseUrl { get; set; }
}
