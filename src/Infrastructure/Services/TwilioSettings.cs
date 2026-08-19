namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied through
/// configuration (user-secrets / environment) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>The account's own sending number, in E.164.</summary>
    public string FromNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address only. When set, it is used verbatim
    /// for every messaging-API call. It does not govern the Lookup API, which lives on another host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
