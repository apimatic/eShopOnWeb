using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    internal const string LookupsClientName = "TwilioLookups";
    internal const string MessagingClientName = "TwilioMessaging";
    internal const string DefaultLookupsBaseUrl = "https://lookups.twilio.com";
    internal const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    internal static AuthenticationHeaderValue CreateBasicAuth(TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    internal static void ApplyBasicAuth(HttpRequestMessage request, IOptions<TwilioSettings> options)
    {
        request.Headers.Authorization = CreateBasicAuth(options.Value);
    }

    internal static string MessagingBaseUrl(TwilioSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl.TrimEnd('/');
    }
}
