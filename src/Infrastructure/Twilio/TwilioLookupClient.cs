using System;
using System.Collections.Generic;
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
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(
                $"Phone number lookup failed with HTTP {(int)response.StatusCode}. {payload}",
                response.StatusCode);
        }

        var parsed = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions)
            ?? throw new TwilioApiException("Phone number lookup returned an empty response.");

        IReadOnlyList<string> errors = parsed.ValidationErrors != null
            ? parsed.ValidationErrors
            : Array.Empty<string>();
        return new TwilioLookupResult(parsed.Valid, parsed.PhoneNumber, errors);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
