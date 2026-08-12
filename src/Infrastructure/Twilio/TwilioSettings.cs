namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Strongly-typed binding of the <c>Twilio:</c> configuration section. Values are supplied
/// through configuration / user-secrets and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string CONFIG_SECTION = "Twilio";

    /// <summary>Live account SID (from <c>TWILIO_ACCOUNT_SID</c>).</summary>
    public string AccountSid { get; set; } = string.Empty;

    /// <summary>Live auth token (from <c>TWILIO_AUTH_TOKEN</c>). Secret — never logged or returned.</summary>
    public string AuthToken { get; set; } = string.Empty;

    /// <summary>This application's configured sending number (from <c>TWILIO_FROM_NUMBER</c>).</summary>
    public string FromNumber { get; set; } = string.Empty;

    /// <summary>Messaging Service SID (from <c>TWILIO_MESSAGING_SERVICE_SID</c>), used for scheduled sends.</summary>
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the messaging API base URL. When set it is used verbatim for
    /// every messaging-API call instead of the provider default. Does not govern other
    /// Twilio APIs (such as Lookups).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The default host for the messaging (v2010) API when no override is configured.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>The host for the Lookups v2 API. Not governed by <see cref="BaseUrl"/>.</summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    public string ResolveMessagingBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.TrimEnd('/');
}
