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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates and canonicalises numbers against the Twilio Lookups v2 API
/// (<c>GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}</c>). Lookups is served
/// from its own host and is deliberately not governed by <c>Twilio:BaseUrl</c>.
/// </summary>
public class TwilioLookupsClient : IPhoneNumberLookupService
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupsClient> _logger;

    public TwilioLookupsClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioLookupsClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return PhoneNumberLookupResult.Invalid("A phone number is required.");

        var url = $"{TwilioSettings.LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Twilio Lookups request failed to reach the provider.");
            return PhoneNumberLookupResult.Invalid("The number could not be validated with the provider.");
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return PhoneNumberLookupResult.Invalid("The provider does not recognise this number as a valid destination.");

            if (!response.IsSuccessStatusCode)
            {
                // Do not log the number; log the operation outcome only.
                _logger.LogWarning("Twilio Lookups returned {Status} for a number-validation request.", (int)response.StatusCode);
                return PhoneNumberLookupResult.Invalid("The number could not be validated with the provider.");
            }

            LookupResponse? lookup;
            try
            {
                lookup = JsonSerializer.Deserialize<LookupResponse>(payload);
            }
            catch (JsonException)
            {
                return PhoneNumberLookupResult.Invalid("The provider returned an unreadable validation response.");
            }

            if (lookup is null)
                return PhoneNumberLookupResult.Invalid("The provider returned no validation result.");

            if (lookup.Valid && !string.IsNullOrWhiteSpace(lookup.PhoneNumber))
                return PhoneNumberLookupResult.Valid(lookup.PhoneNumber!);

            var reason = lookup.ValidationErrors is { Count: > 0 }
                ? $"The provider does not consider this a usable destination ({string.Join(", ", lookup.ValidationErrors)})."
                : "The provider does not consider this a usable destination.";
            return PhoneNumberLookupResult.Invalid(reason);
        }
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var raw = Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
