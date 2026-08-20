namespace Microsoft.eShopWeb.ApplicationCore;

public class TwilioSettings
{
    public const string SectionName = "Twilio";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultMessagingBaseUrl : BaseUrl.TrimEnd('/');
}
