namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioSettings
{
    string AccountSid { get; }
    string AuthToken { get; }
    string FromNumber { get; }
    string MessagingServiceSid { get; }
    string? BaseUrl { get; }
}
