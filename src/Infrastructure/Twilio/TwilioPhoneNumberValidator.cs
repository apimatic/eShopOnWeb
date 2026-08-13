using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates and canonicalises a phone number through Twilio's Lookups v2 API. Lookups is served from a
/// different host than the messaging API, so it is not governed by the messaging base-URL override; the
/// base address is configured on the injected <see cref="HttpClient"/>. The number is never logged.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupPath = "v2/PhoneNumbers/{0}";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = string.Format(System.Globalization.CultureInfo.InvariantCulture, LookupPath, Uri.EscapeDataString(phoneNumber));
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Twilio returns 404 when the value isn't a resolvable number — treat as "not a usable destination".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_FOUND" });
        }

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body);
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(body, JsonOptions);
        if (lookup is null)
        {
            throw new TwilioApiException(0, null, "The Twilio lookup response could not be parsed.");
        }

        var isValid = lookup.Valid ?? false;
        var canonical = lookup.PhoneNumber; // the provider's canonical E.164 form
        var errors = lookup.ValidationErrors ?? new System.Collections.Generic.List<string>();

        return new PhoneNumberValidationResult(isValid, isValid ? canonical : null, errors);
    }

    private static TwilioApiException BuildException(HttpStatusCode statusCode, string body)
    {
        TwilioErrorResponse? error = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorResponse>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON error body; fall back below.
            }
        }

        var message = error?.Message ?? $"Twilio Lookups returned {(int)statusCode}.";
        return new TwilioApiException((int)statusCode, error?.Code, message, error?.MoreInfo);
    }
}
