using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void ConfigureBasicAuth(HttpClient client, TwilioSettings settings)
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
    }

    public static Uri MessagingBaseAddress(TwilioSettings settings)
    {
        var raw = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://api.twilio.com"
            : settings.BaseUrl.Trim();
        return new Uri(raw.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? errorCode = null;
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                var error = JsonSerializer.Deserialize<TwilioErrorBody>(payload, JsonOptions);
                errorCode = error?.Code;
            }
        }
        catch (JsonException)
        {
            // The provider body is not used beyond the numeric code.
        }

        throw new TwilioMessagingException(errorCode, (int)response.StatusCode);
    }

    private sealed class TwilioErrorBody
    {
        public int? Code { get; set; }
        public int? Status { get; set; }
    }
}
