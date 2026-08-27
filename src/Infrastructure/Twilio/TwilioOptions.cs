namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Settings bound from the "Twilio" configuration section. Values are supplied
/// via user-secrets/environment at runtime; none are committed to the repo.
/// </summary>
public class TwilioOptions
{
    public const string SectionName = "Twilio";

    /// <summary>Default host of the Twilio messaging API (api.twilio.com), per the OpenAPI spec's servers.</summary>
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    /// <summary>Host of the Twilio Lookups API, per its OpenAPI spec's servers. Not governed by BaseUrl.</summary>
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;

    /// <summary>Optional override for the messaging API base address only. Used verbatim when set.</summary>
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl!;
}
