using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for the Twilio Lookups API, built against the OpenAPI
/// specification in api-specs/twilio/twilio_lookups_v2 (FetchPhoneNumber).
/// Lookups is served from its own host; the Twilio:BaseUrl messaging override
/// does not govern it.
/// </summary>
public class TwilioLookupClient : IPhoneNumberLookup
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        var twilioSettings = settings.Value;

        if (string.IsNullOrWhiteSpace(twilioSettings.AccountSid) || string.IsNullOrWhiteSpace(twilioSettings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are missing. Bind the 'Twilio' section (AccountSid, AuthToken, FromNumber) from user-secrets or environment variables.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{twilioSettings.AccountSid}:{twilioSettings.AuthToken}")));
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // FetchPhoneNumber: GET /v2/PhoneNumbers/{PhoneNumber}
        using var response = await _httpClient.GetAsync(
            $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorDto? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<TwilioErrorDto>(cancellationToken: cancellationToken);
            }
            catch
            {
                // Fall through to a generic error below.
            }
            throw new TwilioApiException(response.StatusCode, error?.Code,
                error?.Message ?? $"Provider returned {(int)response.StatusCode}.", error?.MoreInfo);
        }

        var dto = await response.Content.ReadFromJsonAsync<TwilioLookupResponseDto>(cancellationToken: cancellationToken);
        if (dto is null)
        {
            throw new TwilioApiException(response.StatusCode, null, "Empty response body from the provider.", null);
        }

        System.Collections.Generic.IReadOnlyList<string> errors =
            dto.ValidationErrors is null ? Array.Empty<string>() : dto.ValidationErrors;

        return new PhoneNumberLookupResult(dto.Valid, dto.Valid ? dto.PhoneNumber : null, errors);
    }
}
