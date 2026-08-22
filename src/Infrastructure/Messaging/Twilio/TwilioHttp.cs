using System;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

public class TwilioClientException : Exception
{
    public int StatusCode { get; }
    public int? ProviderCode { get; }

    public TwilioClientException(int statusCode, int? providerCode)
        : base($"Twilio request failed with HTTP {statusCode}" + (providerCode is null ? "." : $" (code {providerCode})."))
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
    }
}

internal static class TwilioJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
    };
}

internal static class TwilioAuth
{
    public static System.Net.Http.Headers.AuthenticationHeaderValue CreateHeader(string accountSid, string authToken)
    {
        var raw = $"{accountSid}:{authToken}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(raw));
        return new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);
    }
}

internal static class TwilioUri
{
    public static Uri MessagingBaseAddress(string? configuredBaseUrl)
    {
        var value = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://api.twilio.com/"
            : configuredBaseUrl.Trim();

        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    public static string CombineRelative(Uri baseAddress, string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }
}
