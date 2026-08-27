using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio Lookup API, built against
/// api-specs/twilio/twilio_lookups_v2/twilio_lookups_v2.yaml. Served from
/// lookups.twilio.com — the Twilio:BaseUrl messaging override does not apply.
/// </summary>
public class TwilioLookupClient
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        var credentials = settings.Value;
        _httpClient.BaseAddress = new Uri(LookupBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.AccountSid}:{credentials.AuthToken}")));
    }

    /// <summary>GET /v2/PhoneNumbers/{PhoneNumber}. Returns null when the
    /// provider has no such number (HTTP 404).</summary>
    public async Task<TwilioLookupResource?> FetchPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Do not echo the response body: it may reference the number looked up.
            throw new TextMessageProviderException(
                $"Twilio lookup API request failed with HTTP {(int)response.StatusCode}.");
        }

        return JsonSerializer.Deserialize<TwilioLookupResource>(content, JsonOptions);
    }
}
