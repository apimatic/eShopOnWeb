using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

internal static class TwilioHttp
{
    private static readonly Regex PhonePattern = new(@"\+?\d[\d\-\s\(\)]{6,}\d", RegexOptions.Compiled);

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void ApplyAuth(HttpClient httpClient, TwilioSettings settings)
    {
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return PhonePattern.Replace(value, "[redacted]");
    }
}

internal sealed class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? errorCode)
        : base($"Twilio request failed with HTTP {statusCode}{(errorCode.HasValue ? $" (code {errorCode})" : string.Empty)}.")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
    }

    public int StatusCode { get; }
    public int? ErrorCode { get; }
}

internal sealed class TwilioErrorPayload
{
    public int? Code { get; set; }
    public int? Status { get; set; }
}
