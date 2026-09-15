using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Validates and canonicalises numbers through Twilio's Lookup v2 API. Lookup lives on its own host
/// (lookups.twilio.com), which the messaging <see cref="TwilioSettings.BaseUrl"/> override does not
/// govern — so this always targets the Lookup host directly.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com/v2/PhoneNumbers/";

    private readonly HttpClient _http;

    public TwilioPhoneNumberValidator(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        var value = settings.Value;
        var credentials = System.Convert.ToBase64String(Encoding.UTF8.GetBytes($"{value.AccountSid}:{value.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var url = LookupBaseUrl + Uri.EscapeDataString(rawNumber);
        using var response = await _http.GetAsync(url, cancellationToken);

        // Lookup returns 404 for a number it cannot resolve at all — treat that as "not a usable destination".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            int? twilioCode = null;
            try
            {
                using var errorDoc = JsonDocument.Parse(payload);
                if (errorDoc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
                {
                    twilioCode = code.GetInt32();
                }
            }
            catch (JsonException)
            {
                // non-JSON error body — status code alone is enough
            }
            throw new TwilioApiException((int)response.StatusCode, twilioCode, "lookup");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl)
            && validEl.ValueKind is JsonValueKind.True or JsonValueKind.False
            && validEl.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var phoneEl) && phoneEl.ValueKind == JsonValueKind.String
            ? phoneEl.GetString()
            : null;

        return new PhoneNumberValidationResult(valid, valid ? canonical : null);
    }
}
