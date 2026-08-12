using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates and canonicalises phone numbers via the Twilio Lookup v2 API. Lookup lives on its own
/// host (lookups.twilio.com), so this client is configured with that fixed base address and is NOT
/// governed by the messaging base-URL override. The injected <see cref="HttpClient"/> carries the
/// basic-auth header; request logging is disabled so a shopper's number never reaches a log.
/// </summary>
public class TwilioLookupClient : IPhoneNumberValidator
{
    private readonly HttpClient _http;

    public TwilioLookupClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Basic Lookup returns the canonical E.164 form and a validity flag by default.
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _http.GetAsync(path, cancellationToken);

        // A number Twilio cannot parse comes back as 404/400; that is simply "not a usable destination".
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberValidation(false, null);
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            string message = $"Twilio lookup failed with status {(int)response.StatusCode}.";
            try
            {
                using var errDoc = JsonDocument.Parse(content);
                if (errDoc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
                {
                    code = c.GetInt32();
                }
                if (errDoc.RootElement.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    message = m.GetString() ?? message;
                }
            }
            catch (JsonException)
            {
                // keep generic message
            }
            throw new TwilioApiException(response.StatusCode, code, TwilioText.RedactNumbers(message)!);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) &&
                    validEl.ValueKind == JsonValueKind.True;

        string? canonical = null;
        if (root.TryGetProperty("phone_number", out var pn) && pn.ValueKind == JsonValueKind.String)
        {
            canonical = pn.GetString();
        }

        if (!valid || string.IsNullOrEmpty(canonical))
        {
            return new PhoneNumberValidation(false, null);
        }

        return new PhoneNumberValidation(true, canonical);
    }
}
