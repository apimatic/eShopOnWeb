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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates phone numbers with the Twilio Lookups v2 API, built against the api-specs contract
/// (<c>GET /v2/PhoneNumbers/{PhoneNumber}</c>). Lookups is served from its own host and is not
/// governed by <c>Twilio:BaseUrl</c> (which overrides only the messaging API).
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        var s = settings.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{s.AccountSid}:{s.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // A malformed / unusable number is reported by the provider as an unusable destination.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "invalid number" });
        }

        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            string? message = null;
            try
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var error = JsonSerializer.Deserialize<TwilioErrorResponse>(errorBody);
                code = error?.Code;
                message = error?.Message;
            }
            catch
            {
                // ignore; surface the status
            }
            throw new TwilioApiException(response.StatusCode, code, message ?? response.ReasonPhrase);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(json);
        if (lookup is null)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "no response from lookup" });
        }

        var isValid = lookup.Valid == true && !string.IsNullOrEmpty(lookup.PhoneNumber);
        IReadOnlyList<string> errors = lookup.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>();
        return new PhoneNumberValidationResult(isValid, lookup.PhoneNumber, errors);
    }
}
