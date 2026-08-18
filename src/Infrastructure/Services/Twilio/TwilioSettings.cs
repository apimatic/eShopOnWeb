namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio configuration, bound from the "Twilio" section. Values are supplied at runtime
/// (user-secrets / environment) and never hard-coded, so the same build runs against a
/// different Twilio account. The auth token is a secret and is never logged or returned.
/// </summary>
public class TwilioSettings
{
    public const string ConfigurationSection = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the base address of the Twilio *messaging* API (the API this
    /// integration sends, reads and reconciles messages through). When set, it is used verbatim
    /// for every messaging-API call. It does not govern other Twilio hosts (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
