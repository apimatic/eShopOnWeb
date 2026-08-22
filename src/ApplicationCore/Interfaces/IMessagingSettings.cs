namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface IMessagingSettings
{
    string FromNumber { get; }
    string AccountSid { get; }
    string MessagingServiceSid { get; }
    string? BaseUrl { get; }
}
