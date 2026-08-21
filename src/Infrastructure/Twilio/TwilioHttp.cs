using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public static class TwilioHttp
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static void ApplyBasicAuth(HttpClient client, TwilioOptions options)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static string MessagingBaseAddress(TwilioOptions options)
    {
        var configured = options.BaseUrl?.Trim();
        if (string.IsNullOrEmpty(configured))
        {
            return "https://api.twilio.com/";
        }

        return configured.EndsWith('/') ? configured : configured + "/";
    }

    internal static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (value is null)
        {
            throw new TwilioClientException((int)response.StatusCode, null, "deserialize");
        }

        return value;
    }

    internal static async Task ThrowForErrorAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        int? providerCode = null;
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorBody>(stream, JsonOptions, cancellationToken);
            providerCode = error?.Code;
        }
        catch (JsonException)
        {
            // Provider error bodies are not required to parse for the caller; HTTP status is enough.
        }

        throw new TwilioClientException((int)response.StatusCode, providerCode, operation);
    }
}
