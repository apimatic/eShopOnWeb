using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookups";
    public const string DefaultBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        ApplyAuth(_httpClient, _options);
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(DefaultBaseUrl);
        }
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookups v2: GET /v2/PhoneNumbers/{PhoneNumber} — host is lookups.twilio.com (not Twilio:BaseUrl).
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadErrorAsync(response, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return new PhoneNumberLookupResult(false, null, Array.Empty<string>());
        }

        var errors = payload.ValidationErrors ?? Array.Empty<string>();
        return new PhoneNumberLookupResult(payload.Valid, payload.PhoneNumber, errors);
    }

    internal static void ApplyAuth(HttpClient client, TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || string.IsNullOrWhiteSpace(options.AuthToken))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static async Task<TwilioApiError?> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<TwilioApiError>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
