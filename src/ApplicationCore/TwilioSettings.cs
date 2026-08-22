namespace Microsoft.eShopWeb.ApplicationCore;

public class TwilioSettings
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public override string ToString() =>
        $"TwilioSettings {{ AccountSid = {AccountSid}, FromNumber = {FromNumber}, MessagingServiceSid = {MessagingServiceSid}, BaseUrl = {BaseUrl} }}";
}
