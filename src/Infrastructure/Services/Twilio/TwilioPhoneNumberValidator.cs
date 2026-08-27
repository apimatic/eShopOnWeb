using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates phone numbers through the provider's Lookup API (lookups.twilio.com/v2).
/// Requested with no Fields, so the formatting/validation call is free. This host is not
/// governed by Twilio:BaseUrl, which only overrides the messaging API.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(HttpClient httpClient, ILogger<TwilioPhoneNumberValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> ValidateAndNormalizeAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        // EscapeDataString percent-encodes a leading '+' as %2B so it survives the URL path.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number lookup failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new TwilioApiException((int)response.StatusCode, null, "Phone number lookup failed.");
        }

        var result = await response.Content.ReadFromJsonAsync<LookupResult>(cancellationToken: cancellationToken);
        if (result is null || !result.Valid || string.IsNullOrEmpty(result.PhoneNumber))
        {
            return null;
        }

        return result.PhoneNumber;
    }

    private sealed class LookupResult
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }
}
