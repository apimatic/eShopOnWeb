namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public interface ITwilioSettingsAccessor
{
    string AccountSid { get; }
    string FromNumber { get; }
    string MessagingServiceSid { get; }
    string? BaseUrl { get; }
}
