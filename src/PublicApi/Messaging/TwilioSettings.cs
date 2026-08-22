namespace Microsoft.eShopWeb.PublicApi.Messaging;

public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string? MessagingServiceSid { get; set; }
    public string? BaseUrl { get; set; }
}
