using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Client for the Twilio Lookups API, built against the authoritative OpenAPI
/// specification (api-specs/twilio/twilio_lookups_v2):
///   GET /v2/PhoneNumbers/{PhoneNumber}
/// served from https://lookups.twilio.com. This host is not governed by Twilio:BaseUrl,
/// which applies to the messaging API only.
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// </summary>
public class TwilioLookupClient : IPhoneNumberValidator
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        var twilioOptions = options.Value;
        if (string.IsNullOrWhiteSpace(twilioOptions.AccountSid) || string.IsNullOrWhiteSpace(twilioOptions.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(LookupsBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilioOptions.AccountSid}:{twilioOptions.AuthToken}")));
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return PhoneNumberValidationResult.Invalid(
                $"The provider could not evaluate the number (status {(int)response.StatusCode}).");
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(content, JsonOptions);
        if (lookup == null)
        {
            return PhoneNumberValidationResult.Invalid("The provider returned an unreadable response.");
        }

        if (lookup.Valid != true || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
        {
            var reasons = lookup.ValidationErrors is { Length: > 0 }
                ? string.Join(", ", lookup.ValidationErrors)
                : "not a usable destination";
            return PhoneNumberValidationResult.Invalid($"The provider does not consider this a usable destination ({reasons}).");
        }

        // Store the provider's own canonical (E.164) form of the number.
        return PhoneNumberValidationResult.Valid(lookup.PhoneNumber);
    }

    // Mirrors components/schemas/LookupResponse from the Lookups v2 specification.
    private sealed class LookupResponse
    {
        [JsonPropertyName("calling_country_code")] public string? CallingCountryCode { get; set; }
        [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
        [JsonPropertyName("valid")] public bool? Valid { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }
}
