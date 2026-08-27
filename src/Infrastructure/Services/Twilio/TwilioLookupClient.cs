using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for the Twilio Lookups API, built against the
/// twilio_lookups_v2 OpenAPI document (the authoritative contract):
///   GET /v2/PhoneNumbers/{PhoneNumber}   FetchPhoneNumber
/// Served from https://lookups.twilio.com — the Twilio:BaseUrl override governs
/// the messaging API only, never this host.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        var settings = options.Value;
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<TwilioLookupResult> FetchPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new TwilioLookupResult { Valid = false };
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(response.StatusCode, null, "Phone number lookup failed.");
        }
        return JsonSerializer.Deserialize<TwilioLookupResult>(content, JsonOptions)
            ?? new TwilioLookupResult { Valid = false };
    }
}
