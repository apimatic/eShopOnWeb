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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written client for the Twilio Lookups API, built against the authoritative
/// OpenAPI contract in api-specs/twilio/twilio_lookups_v2:
///   GET /v2/PhoneNumbers/{PhoneNumber}  (FetchPhoneNumber)
/// Served from https://lookups.twilio.com — the Twilio:BaseUrl override governs only
/// the messaging API and does not apply here.
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// </summary>
public class TwilioLookupsClient : IPhoneNumberLookup
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupsClient> _logger;

    public TwilioLookupsClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioLookupsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.Value.AccountSid}:{settings.Value.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider cannot parse the input as a phone number at all.
            return new PhoneNumberLookupResult(false, null, "The provider does not recognize this as a phone number.");
        }

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResource? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResource>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall through to a generic provider exception.
            }

            _logger.LogWarning("Twilio lookups API call failed with HTTP {StatusCode}, provider error code {ErrorCode}.",
                (int)response.StatusCode, error?.Code);
            throw new ProviderException(error?.Message ?? $"Twilio Lookups API error (HTTP {(int)response.StatusCode}).", error?.Code, (int)response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<LookupPhoneNumberResource>(payload, JsonOptions);
        if (result is null)
        {
            throw new ProviderException("Twilio Lookups API returned an unreadable response.");
        }

        var validationError = result.ValidationErrors is { Length: > 0 }
            ? string.Join(", ", result.ValidationErrors)
            : null;

        return new PhoneNumberLookupResult(result.Valid, result.Valid ? result.PhoneNumber : null, validationError);
    }

    private class LookupPhoneNumberResource
    {
        [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }
}
