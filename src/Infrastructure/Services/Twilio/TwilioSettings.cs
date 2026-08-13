namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Strongly-typed view of the <c>Twilio:</c> configuration section. Values are supplied via
/// user-secrets / environment configuration — never hard-coded. The auth token is a secret and
/// is never logged or surfaced.
/// </summary>
public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's own sending number (E.164). Reconciliation counts only its traffic.</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging service SID, used for scheduled sends.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base address. When set it is used verbatim for
    /// every messaging-API call; when blank the provider default is used. Does not govern the
    /// separate Lookups host used for number validation.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool HasBaseUrlOverride => !string.IsNullOrWhiteSpace(BaseUrl);
}
