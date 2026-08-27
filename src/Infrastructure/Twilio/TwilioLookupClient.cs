using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's Lookups API, built to the OpenAPI contract in
/// api-specs/twilio/twilio_lookups_v2 (GET /v2/PhoneNumbers/{PhoneNumber}). Lookups is
/// served from lookups.twilio.com — the Twilio:BaseUrl messaging override does not
/// apply here.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        var twilioSettings = settings.Value;
        if (string.IsNullOrWhiteSpace(twilioSettings.AccountSid) || string.IsNullOrWhiteSpace(twilioSettings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are missing. Provide Twilio:AccountSid and Twilio:AuthToken via user-secrets or environment variables.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(LookupsBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilioSettings.AccountSid}:{twilioSettings.AuthToken}")));
    }

    public async Task<TwilioPhoneNumberLookup> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new TwilioPhoneNumberLookup { IsValid = false };
        }

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResource? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResource>(content, TwilioJson.Options);
            }
            catch (JsonException)
            {
                // fall through to a generic error below
            }

            throw new TwilioApiException((int)response.StatusCode, error?.Code,
                error?.Message ?? "Unexpected response from Twilio Lookups.");
        }

        var resource = JsonSerializer.Deserialize<TwilioLookupPhoneNumberResource>(content, TwilioJson.Options);
        return new TwilioPhoneNumberLookup
        {
            IsValid = resource?.Valid ?? false,
            CanonicalPhoneNumber = resource?.PhoneNumber,
            NationalFormat = resource?.NationalFormat,
            CountryCode = resource?.CountryCode,
            ValidationErrors = resource?.ValidationErrors ?? new List<string>()
        };
    }
}
