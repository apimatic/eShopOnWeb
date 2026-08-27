using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Phone number validation against the Twilio Lookups API, per the authoritative
/// OpenAPI specification in api-specs/twilio/twilio_lookups_v2:
///   GET /v2/PhoneNumbers/{PhoneNumber}   FetchPhoneNumber
/// Served from https://lookups.twilio.com (the messaging BaseUrl override does not
/// apply here). Auth: HTTP Basic with AccountSid:AuthToken.
/// </summary>
public class TwilioPhoneNumberLookupService : IPhoneNumberLookupService
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberLookupService(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var uri = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, null);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            string? detail = null;
            try
            {
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
                {
                    code = codeEl.GetInt32();
                }
                if (doc.RootElement.TryGetProperty("message", out var msgEl))
                {
                    detail = msgEl.GetString();
                }
            }
            catch (JsonException)
            {
            }
            throw new MessageProviderException((int)response.StatusCode, code, detail);
        }

        var result = JsonSerializer.Deserialize<LookupResponse>(content);
        return new PhoneNumberLookupResult(result?.Valid == true, result?.PhoneNumber, result?.CountryCode);
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    }
}
