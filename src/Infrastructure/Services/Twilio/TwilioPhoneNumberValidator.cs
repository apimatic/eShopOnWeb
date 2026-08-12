using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates and canonicalises a phone number with the provider's Lookup API. Lookup is served from
/// its own host (lookups.twilio.com) and is deliberately NOT governed by <see cref="TwilioSettings.BaseUrl"/>,
/// which overrides only the messaging API. A basic (free) lookup returns both the validity verdict and
/// the canonical E.164 form to store. The destination number is never logged.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _http;

    public TwilioPhoneNumberValidator(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        var s = settings.Value;
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{s.AccountSid}:{s.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // The number sits in the URL path; a leading '+' must be percent-encoded (EscapeDataString does this).
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber ?? string.Empty)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        // A number that fails validation still comes back 200 with valid:false — that is the reject path,
        // not an error. A genuinely malformed path (404) is treated as an invalid number too.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return PhoneNumberValidationResult.Invalid(new List<string> { "NOT_A_NUMBER" });

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Phone number validation failed: provider returned {(int)response.StatusCode}.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            var errors = new List<string>();
            if (root.TryGetProperty("validation_errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errs.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String)
                        errors.Add(e.GetString()!);
            }
            if (errors.Count == 0)
                errors.Add("INVALID");
            return PhoneNumberValidationResult.Invalid(errors);
        }

        var canonical = root.TryGetProperty("phone_number", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString()
            : null;

        if (string.IsNullOrEmpty(canonical))
            throw new InvalidOperationException("Phone number validation succeeded but no canonical number was returned.");

        return PhoneNumberValidationResult.Valid(canonical!);
    }
}
