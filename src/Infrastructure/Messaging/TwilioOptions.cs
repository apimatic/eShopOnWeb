using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string FromNumber { get; set; } = string.Empty;
    public string MessagingServiceSid { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(AccountSid) ||
            string.IsNullOrWhiteSpace(AuthToken) ||
            string.IsNullOrWhiteSpace(FromNumber) ||
            string.IsNullOrWhiteSpace(MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio messaging is not configured.");
        }
    }
}
