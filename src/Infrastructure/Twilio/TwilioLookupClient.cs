using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookups";
    public const string DefaultBaseUrl = "https://lookups.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> options)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
    }

    public async Task<PhoneNumberLookupResult> FetchPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(path));
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException((int)response.StatusCode, $"Twilio lookup failed with status {(int)response.StatusCode}.");
        }

        var payload = JsonSerializer.Deserialize<TwilioLookupResponse>(body, TwilioJson.Options);
        if (payload == null)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        return new PhoneNumberLookupResult
        {
            Valid = payload.Valid,
            CanonicalNumber = payload.PhoneNumber,
            ValidationErrors = payload.ValidationErrors ?? Array.Empty<string>()
        };
    }

    private static Uri Combine(string path)
    {
        var baseUri = new Uri(DefaultBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        return new Uri(baseUri, path.TrimStart('/'));
    }
}
