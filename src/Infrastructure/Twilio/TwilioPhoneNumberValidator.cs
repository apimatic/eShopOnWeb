using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio's Lookups v2 API, built to the OpenAPI contract in
/// <c>api-specs/twilio/twilio_lookups_v2</c>. Lookups is served from its own host
/// (<c>https://lookups.twilio.com</c>) and is deliberately NOT governed by the messaging
/// <c>Twilio:BaseUrl</c> override. HTTP Basic auth is applied by the configured <see cref="HttpClient"/>.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    public const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IAppLogger<TwilioPhoneNumberValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A number the provider can't even look up is not a usable destination: reject it.
            var message = ExtractErrorMessage(payload) ?? "The number could not be validated.";
            _logger.LogWarning("Lookups rejected a number ({StatusCode}).", (int)response.StatusCode);
            return PhoneValidationResult.Invalid(new[] { message });
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var isValid = root.TryGetProperty("valid", out var validElement)
            && validElement.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var numberElement)
            && numberElement.ValueKind == JsonValueKind.String
                ? numberElement.GetString()
                : null;

        if (!isValid || string.IsNullOrEmpty(canonical))
        {
            var errors = ReadValidationErrors(root);
            _logger.LogWarning("Lookups reported a number as not valid.");
            return PhoneValidationResult.Invalid(errors.Count > 0 ? errors : new[] { "The number is not a valid message destination." });
        }

        // Only trust phone_number as canonical when the number is valid.
        return PhoneValidationResult.Valid(canonical);
    }

    private static IReadOnlyList<string> ReadValidationErrors(JsonElement root)
    {
        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var element) && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value))
                    {
                        errors.Add(value);
                    }
                }
            }
        }
        return errors;
    }

    private static string? ExtractErrorMessage(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                ? message.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
