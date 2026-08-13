namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Bound from the "Twilio:" configuration section. Values are supplied via configuration
/// (environment / user-secrets) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The application's own sending number, in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID, required by the provider to schedule messages for later delivery.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set, it is used verbatim as the base
    /// for every messaging-API call (send / read / update / list). It does not govern other Twilio
    /// hosts such as Lookup.
    /// </summary>
    public string? BaseUrl { get; set; }
}
