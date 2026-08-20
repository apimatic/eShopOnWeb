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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates and canonicalises a phone number using the provider's lookup capability. Lookup is
/// served from its own host and is deliberately NOT governed by the messaging base-URL override.
/// The free formatting/validation is used (no paid data packages requested).
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    // Lookup lives on its own host; the messaging base-URL override does not apply here.
    private static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    private readonly HttpClient _http;

    public TwilioPhoneNumberValidator(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        var settings = options.Value;
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // The number sits in the URL path; the leading '+' of E.164 must be percent-encoded.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";

        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync(url, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MessagingProviderException("Could not reach the phone-number lookup service.", innerException: ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            // v2 returns 200 with valid:false for an invalid number; treat a 404 as invalid too.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });
            }

            if ((int)response.StatusCode is < 200 or >= 300)
            {
                throw new MessagingProviderException($"Phone-number lookup failed (HTTP {(int)response.StatusCode}).");
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var valid = root.TryGetProperty("valid", out var validEl) &&
                        validEl.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                        validEl.GetBoolean();

            string? e164 = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
                ? pnEl.GetString()
                : null;

            var errors = new List<string>();
            if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errEl.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        var s = e.GetString();
                        if (!string.IsNullOrEmpty(s))
                        {
                            errors.Add(s!);
                        }
                    }
                }
            }

            // Guard against a valid:true with no canonical form (do not store a non-canonical value).
            if (valid && string.IsNullOrEmpty(e164))
            {
                valid = false;
            }

            return new PhoneNumberValidationResult(valid, e164, errors);
        }
    }
}
