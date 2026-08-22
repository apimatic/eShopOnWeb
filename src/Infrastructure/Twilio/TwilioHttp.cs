using System;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttp
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public static AuthenticationHeaderValue CreateAuthHeader(TwilioSettings settings)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    public static string EnsureTrailingSlash(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return "https://api.twilio.com/";
        }

        return baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
    }
}
