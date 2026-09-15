using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio Lookup v2 Basic Lookup: GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
/// Confirmed: https://www.twilio.com/docs/lookup/v2-api
/// This host is not governed by Twilio:BaseUrl (messaging API only).
/// </summary>
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
        ApplyBasicAuth(_httpClient, _settings);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/PhoneNumbers/{encoded}");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PhoneNumberLookupResult
            {
                Valid = false,
                ValidationErrors = new[] { $"lookup_http_{(int)response.StatusCode}" }
            };
        }

        var dto = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions);
        return new PhoneNumberLookupResult
        {
            Valid = dto?.Valid == true,
            CanonicalPhoneNumber = dto?.PhoneNumber,
            ValidationErrors = dto?.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>()
        };
    }

    internal static void ApplyBasicAuth(HttpClient httpClient, TwilioSettings settings)
    {
        if (httpClient.DefaultRequestHeaders.Authorization != null)
        {
            return;
        }

        if (string.IsNullOrEmpty(settings.AccountSid) || string.IsNullOrEmpty(settings.AuthToken))
        {
            return;
        }

        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed class LookupResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
