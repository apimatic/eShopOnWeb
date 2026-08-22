using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioHttp
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    public static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var raw = Encoding.UTF8.GetBytes($"{accountSid}:{authToken}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    public static string MessagingRoot(TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return DefaultMessagingBaseUrl;
        }

        return options.BaseUrl.TrimEnd('/');
    }

    public static string MessagesCollectionUrl(TwilioOptions options)
        => $"{MessagingRoot(options)}/2010-04-01/Accounts/{Uri.EscapeDataString(options.AccountSid)}/Messages.json";

    public static string MessageInstanceUrl(TwilioOptions options, string messageSid)
        => $"{MessagingRoot(options)}/2010-04-01/Accounts/{Uri.EscapeDataString(options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    public static string LookupUrl(string phoneNumber)
        => $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
}
