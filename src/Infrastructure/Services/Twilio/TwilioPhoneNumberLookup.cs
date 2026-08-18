using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates and canonicalises a number via Twilio Lookup v2. Served from lookups.twilio.com, a
/// different host than the messaging API, so it is not governed by <c>Twilio:BaseUrl</c>.
/// </summary>
public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberLookup(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // Twilio returns 200 with valid=false for structurally invalid numbers; a 404 also means
        // "not a usable number". Either way we report it as not valid rather than an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, null);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsGatewayException($"Twilio Lookup request failed (HTTP {(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl)
            && validEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            && validEl.GetBoolean();

        string? e164 = root.TryGetProperty("phone_number", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString()
            : null;
        string? national = root.TryGetProperty("national_format", out var nf) && nf.ValueKind == JsonValueKind.String
            ? nf.GetString()
            : null;

        return new PhoneNumberLookupResult(valid, e164, national);
    }
}
