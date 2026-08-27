namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded or written to source files.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_NAME = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Secret: never logged, never returned by an endpoint.</summary>
    public string AuthToken { get; set; } = string.Empty;

    public string FromNumber { get; set; } = string.Empty;
    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used
    /// verbatim for every messaging-API call instead of the provider default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
