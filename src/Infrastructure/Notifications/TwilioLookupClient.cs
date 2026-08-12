using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Validates a phone number through Twilio's Lookups v2 API and returns the provider's canonical
/// E.164 form. Lookups is served from its own host (lookups.twilio.com), which the messaging
/// <c>Twilio:BaseUrl</c> override does not govern.
/// </summary>
public class TwilioLookupClient : IPhoneNumberValidationService
{
    private readonly HttpClient _http;

    public TwilioLookupClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPhoneNumber))
        {
            return new PhoneNumberValidationResult(false, null);
        }

        var url = $"{TwilioSettings.LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber.Trim())}";
        using var response = await _http.GetAsync(url, cancellationToken);

        // The provider signals an unusable/unparseable number with 404 or 400. Treat those as "not a
        // usable destination" rather than as a transport failure.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberValidationResult(false, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            throw new TwilioApiException($"Twilio lookup failed (HTTP {status}).", status);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl)
                    && validEl.ValueKind is JsonValueKind.True or JsonValueKind.False
                    && validEl.GetBoolean();

        var canonical = root.TryGetProperty("phone_number", out var numberEl) && numberEl.ValueKind == JsonValueKind.String
            ? numberEl.GetString()
            : null;

        return valid && !string.IsNullOrWhiteSpace(canonical)
            ? new PhoneNumberValidationResult(true, canonical)
            : new PhoneNumberValidationResult(false, null);
    }
}
