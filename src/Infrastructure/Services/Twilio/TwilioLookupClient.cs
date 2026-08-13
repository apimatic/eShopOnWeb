using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates numbers through the provider's Lookup v2 API. Lookup is served from its own host
/// (<see cref="TwilioSettings.LookupBaseUrl"/>) and is deliberately not affected by the messaging
/// base-url override. A number the provider does not consider valid is reported as such so it can
/// be rejected at registration time.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{TwilioSettings.LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // A grossly malformed number is reported by the provider as a 404 — treat it as invalid
        // rather than an error, since the caller's intent (is this a usable destination?) is answered.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneLookupResult(false, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw ParseError((int)response.StatusCode, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validElement) &&
                    validElement.ValueKind == JsonValueKind.True;

        string? canonical = null;
        if (root.TryGetProperty("phone_number", out var numberElement) && numberElement.ValueKind == JsonValueKind.String)
        {
            canonical = numberElement.GetString();
        }

        return new PhoneLookupResult(valid, valid ? canonical : null);
    }

    private static TwilioApiException ParseError(int statusCode, string payload)
    {
        int? code = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
            {
                code = c.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return new TwilioApiException(statusCode, code, null);
    }
}
