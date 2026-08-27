using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri(LookupBaseUrl);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        var payload = await response.Content.ReadFromJsonAsync<LookupResponse>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        return new PhoneNumberLookupResult
        {
            Valid = payload.Valid,
            CanonicalPhoneNumber = payload.PhoneNumber,
            ValidationErrors = payload.ValidationErrors ?? []
        };
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }
}
