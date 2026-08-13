using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Validates a phone number with the Twilio Lookups v2 API and returns its canonical E.164 form.
/// Built to the OpenAPI contract in api-specs/twilio/twilio_lookups_v2: HTTP Basic auth and
/// GET <c>/v2/PhoneNumbers/{PhoneNumber}</c>. Lookups is served from its own host and is not
/// governed by the messaging <c>Twilio:BaseUrl</c> override.
/// </summary>
public class TwilioLookupClient : IPhoneNumberValidator
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        var settings = options.Value;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // A number the provider cannot resolve at all is not a usable destination.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneValidationResult(false, null, new[] { "The number could not be found." });
        }

        if (!response.IsSuccessStatusCode)
        {
            return new PhoneValidationResult(false, null,
                new[] { $"The number could not be validated (provider returned {(int)response.StatusCode})." });
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(json, JsonOptions);

        if (lookup is null)
        {
            return new PhoneValidationResult(false, null, new[] { "The provider returned an empty lookup response." });
        }

        if (!lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            IReadOnlyList<string> errors = lookup.ValidationErrors is { Count: > 0 }
                ? lookup.ValidationErrors
                : new[] { "The number is not a valid, reachable destination." };
            return new PhoneValidationResult(false, null, errors);
        }

        // Store the provider's own canonical E.164 form, not whatever the caller typed.
        return new PhoneValidationResult(true, lookup.PhoneNumber, Array.Empty<string>());
    }
}
