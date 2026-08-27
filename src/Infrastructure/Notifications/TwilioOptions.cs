namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Secrets are supplied via
/// user-secrets/environment variables, never from files in the repository.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim
    /// for every messaging-API call. Does not govern other Twilio capabilities (e.g. Lookup).
    /// </summary>
    public string? BaseUrl { get; set; }
}
