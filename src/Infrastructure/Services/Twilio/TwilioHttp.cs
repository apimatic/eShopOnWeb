using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    public static AuthenticationHeaderValue BasicAuth(TwilioSettings settings)
    {
        var raw = $"{settings.AccountSid}:{settings.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    public static string MessagingBaseUrl(TwilioSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            return settings.BaseUrl.TrimEnd('/');
        }

        return "https://api.twilio.com";
    }

    public static string ResolveMessagingUri(TwilioSettings settings, string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            var baseUrl = MessagingBaseUrl(settings);
            return Combine(baseUrl, absolute.PathAndQuery);
        }

        return Combine(MessagingBaseUrl(settings), pathOrUri);
    }

    public static string Combine(string baseUrl, string pathAndQuery)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var path = pathAndQuery.StartsWith('/') ? pathAndQuery : "/" + pathAndQuery;
        var baseUri = new Uri(trimmedBase.Contains("://") ? trimmedBase : "https://" + trimmedBase, UriKind.Absolute);
        if (baseUri.AbsolutePath is "/" or "")
        {
            return new Uri(baseUri, path).ToString();
        }

        return trimmedBase + path;
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? string.Empty;
        }

        return System.Text.RegularExpressions.Regex.Replace(value, @"\+?\d{8,15}", "[redacted]");
    }
}
