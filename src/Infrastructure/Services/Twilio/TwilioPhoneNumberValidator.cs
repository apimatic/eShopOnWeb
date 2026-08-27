using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates phone numbers through the provider's Lookup API (lookups.twilio.com/v2).
/// A lookup with no Fields requested is free and returns whether the number is valid
/// plus its canonical E.164 form. The Lookup API is served from its own host and is
/// not governed by the Twilio:BaseUrl messaging override.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        var twilioSettings = settings.Value;
        twilioSettings.Validate();

        _httpClient.BaseAddress = new Uri(LookupBaseUrl);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilioSettings.AccountSid}:{twilioSettings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        // EscapeDataString percent-encodes a leading '+' as %2B, which the path parameter requires.
        var url = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false };
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(response.StatusCode, null,
                $"Phone number validation failed with status {(int)response.StatusCode}.");
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(content, JsonOptions);

        return new PhoneNumberValidationResult
        {
            IsValid = lookup?.Valid ?? false,
            CanonicalNumber = lookup?.PhoneNumber,
            NationalFormat = lookup?.NationalFormat,
            ValidationErrors = lookup?.ValidationErrors ?? new List<string>()
        };
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
        [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
    }
}
