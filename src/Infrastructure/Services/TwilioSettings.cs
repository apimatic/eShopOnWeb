namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio messaging configuration, bound from the <c>Twilio</c> configuration section. Values are supplied
/// by configuration only (environment / user-secrets) and are never hard-coded. The auth token is a secret:
/// it is never logged, never returned by an endpoint, and never written into a source file.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim for every
    /// messaging-API call; it does not govern other Twilio hosts (e.g. Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }
}
