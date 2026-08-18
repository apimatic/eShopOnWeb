using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Validates candidate numbers with the Twilio Lookup v2 API and returns the provider's canonical E.164 form.
/// Lookup is a distinct capability on its own host (not the messaging base address), so it is never governed
/// by <c>Twilio:BaseUrl</c>. A free-tier lookup (no Fields) is used purely for formatting and validation.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly HttpClient _http;

    public TwilioPhoneNumberValidator(HttpClient http)
    {
        _http = http;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });

        // The raw input (E.164 or national) goes straight into the path; a leading '+' must be percent-encoded.
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber.Trim())}";
        if (!string.IsNullOrWhiteSpace(countryCode))
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.Trim())}";

        using var response = await _http.GetAsync(path, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string? code = null, message = null;
            try
            {
                using var errDoc = JsonDocument.Parse(content);
                code = GetString(errDoc.RootElement, "code");
                message = GetString(errDoc.RootElement, "message");
            }
            catch (JsonException) { /* non-JSON body */ }

            var detail = message is not null ? $": {PhoneRedactor.Redact(message)}" : string.Empty;
            var codePart = code is not null ? $" (code {code})" : string.Empty;
            throw new TwilioApiException($"Twilio Lookup API returned {(int)response.StatusCode}{codePart}{detail}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var e164 = GetString(root, "phone_number");
        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errs.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);
        }

        // Treat a valid verdict without a canonical form defensively as not usable.
        if (valid && string.IsNullOrEmpty(e164))
            return new PhoneNumberValidationResult(false, null, new[] { "NO_CANONICAL_FORM" });

        return new PhoneNumberValidationResult(valid, valid ? e164 : null, errors);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
