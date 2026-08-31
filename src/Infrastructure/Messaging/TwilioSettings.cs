namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Bound from the "Twilio" configuration section. Values arrive via
/// user-secrets / environment variables and are never committed to the repo.
/// </summary>
public class TwilioSettings
{
    public const string ConfigName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set it
    /// is used verbatim for every messaging-API call; other Twilio
    /// capabilities (e.g. Lookup) keep their own hosts.
    /// </summary>
    public string? BaseUrl { get; set; }
}
