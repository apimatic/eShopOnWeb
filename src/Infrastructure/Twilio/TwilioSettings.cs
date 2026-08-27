namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets
/// or environment variables; none are hard-coded or committed.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret: never logged, never returned by an endpoint.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number (E.164).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Required for provider-held scheduled messages.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API host only. When set, used verbatim
    /// as the base address for every messaging-API call. Does not govern other Twilio
    /// capabilities (e.g. Lookup), which keep their own hosts.</summary>
    public string? BaseUrl { get; set; }
}
