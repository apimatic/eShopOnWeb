using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates phone numbers against the Twilio Lookups API, built against
/// api-specs/twilio/twilio_lookups_v2/twilio_lookups_v2.yaml:
///   GET /v2/PhoneNumbers/{PhoneNumber}  (FetchPhoneNumber)
/// Returns the provider's canonical (E.164) form of valid numbers.
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
        using var response = await _httpClient.GetAsync(
            $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);

        // The Lookups API answers 404 for numbers that are not valid.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, "The provider does not consider this a usable phone number.");
        }

        if (!response.IsSuccessStatusCode)
        {
            int? providerCode = null;
            try
            {
                var error = await response.Content.ReadFromJsonAsync<TwilioErrorResource>(cancellationToken: cancellationToken);
                providerCode = error?.Code;
            }
            catch
            {
                // Non-standard error payload; the HTTP status is enough.
            }
            throw new MessagingProviderException((int)response.StatusCode, providerCode, "FetchPhoneNumber");
        }

        var resource = await response.Content.ReadFromJsonAsync<TwilioPhoneNumberResource>(cancellationToken: cancellationToken);
        if (resource is null || !resource.Valid || string.IsNullOrWhiteSpace(resource.PhoneNumber))
        {
            var reason = resource?.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", resource.ValidationErrors)
                : "The provider does not consider this a usable phone number.";
            return new PhoneNumberValidationResult(false, null, reason);
        }

        return new PhoneNumberValidationResult(true, resource.PhoneNumber, null);
    }
}
