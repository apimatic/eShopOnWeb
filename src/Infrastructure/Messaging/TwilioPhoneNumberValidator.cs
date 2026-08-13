using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// <see cref="IPhoneNumberValidator"/> implemented against the Twilio Lookups V2 API
/// (<c>GET /v2/PhoneNumbers/{PhoneNumber}</c>) exactly as described by that spec. The response's <c>valid</c>
/// flag decides whether the number is a usable destination, and <c>phone_number</c> is the provider's canonical
/// (E.164) form that gets stored. Lookups is served from its own host and is not governed by the messaging
/// <c>Twilio:BaseUrl</c> override.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberValidator(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
            return PhoneNumberValidationResult.Invalid("A phone number is required.");

        // The path segment is the number as supplied; Lookups resolves it to canonical form.
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // A structurally unusable number is reported by Lookups as a 404 (not-found) rather than a 200 with
        // valid=false; treat that as "not a usable destination" rather than a transport failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return PhoneNumberValidationResult.Invalid("The number is not a valid, reachable destination.");

        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            string? message = null;
            try
            {
                var error = JsonSerializer.Deserialize<TwilioErrorDto>(payload);
                code = error?.Code;
                message = error?.Message;
            }
            catch (JsonException) { /* fall through */ }
            throw new TwilioApiException(response.StatusCode, code, message);
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupDto>(payload)
            ?? throw new TwilioApiException(response.StatusCode, null, "Lookup response could not be parsed.");

        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
            return PhoneNumberValidationResult.Invalid("The number is not a valid, reachable destination.");

        return PhoneNumberValidationResult.Valid(lookup.PhoneNumber);
    }
}
