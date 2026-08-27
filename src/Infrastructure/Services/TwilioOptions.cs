namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioOptions
{
    public const string SectionName = "Twilio";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
