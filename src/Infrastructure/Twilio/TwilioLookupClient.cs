using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A hand-written client for the provider's Lookups v2 PhoneNumber resource, built against the contract
/// in <c>api-specs/twilio/twilio_lookups_v2</c>. Lookups is served from its own host and is not
/// governed by the <c>Twilio:BaseUrl</c> messaging override. Auth is the same HTTP Basic credentials.
/// </summary>
public class TwilioLookupClient
{
    private readonly HttpClient _http;

    public TwilioLookupClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>GET /v2/PhoneNumbers/{PhoneNumber} — validate and canonicalize a number.</summary>
    public async Task<TwilioLookupResponse> LookupAsync(string rawNumber, CancellationToken ct)
    {
        var url = $"{TwilioSettings.LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber.Trim())}";
        using var response = await _http.GetAsync(url, ct);

        // The provider answers 404 for a number it cannot even parse; treat that as "not valid" rather
        // than a transport failure so registration rejects it cleanly.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new TwilioLookupResponse { Valid = false, ValidationErrors = new() { "NOT_A_NUMBER" } };

        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(ct);
            TwilioErrorResponse? error = null;
            try { error = JsonSerializer.Deserialize<TwilioErrorResponse>(raw, TwilioJson.Options); }
            catch { /* not the provider's error model */ }
            var message = error?.Message ?? $"The provider returned HTTP {(int)response.StatusCode} validating the number.";
            throw new SmsGatewayException(PhoneNumberRedactor.Scrub(message), error?.Code);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<TwilioLookupResponse>(stream, TwilioJson.Options, ct)
               ?? new TwilioLookupResponse { Valid = false };
    }
}
