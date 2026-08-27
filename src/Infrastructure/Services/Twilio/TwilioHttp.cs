using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    public static void ApplyBasicAuth(HttpRequestMessage request, TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.ParseAdd("application/json");
    }

    public static string MessagingBaseUrl(TwilioSettings settings)
    {
        return string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://api.twilio.com"
            : settings.BaseUrl.TrimEnd('/');
    }

    public static string Combine(string baseUrl, string pathAndQuery)
    {
        if (string.IsNullOrEmpty(pathAndQuery))
        {
            return baseUrl;
        }

        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out var absolute))
        {
            return baseUrl.TrimEnd('/') + absolute.PathAndQuery;
        }

        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return baseUrl.TrimEnd('/') + pathAndQuery;
    }

    public static void EnsureConfigured(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new TwilioMessagingException("Twilio AccountSid and AuthToken are not configured.");
        }
    }
}
