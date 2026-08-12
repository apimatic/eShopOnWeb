namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio account/configuration, bound from the "Twilio" configuration section. Values are supplied
/// via configuration (user-secrets / environment) and are never hard-coded.
/// </summary>
public class TwilioSettings
{
    public const string ConfigSection = "Twilio";

    /// <summary>Default host for the messaging (api.twilio.com) API when <see cref="BaseUrl"/> is unset.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>Host for the Lookups API. Not governed by <see cref="BaseUrl"/> (a different Twilio host).</summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    public string? AccountSid { get; set; }

    public string? AuthToken { get; set; }

    public string? FromNumber { get; set; }

    public string? MessagingServiceSid { get; set; }

    /// <summary>
    /// Optional override for the messaging API base URL only. When set, used verbatim as the base
    /// address for every messaging-API call. Does not affect the Lookups API.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective messaging base URL, normalised to end with a slash.</summary>
    public string MessagingBaseUrl
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!.Trim();
            return value.EndsWith('/') ? value : value + "/";
        }
    }
}
