using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// <see cref="IPhoneNumberValidator"/> implemented against the Twilio Lookups v2 API
/// (lookups.twilio.com, <c>GET /v2/PhoneNumbers/{PhoneNumber}</c>) as described by the
/// <c>twilio_lookups_v2</c> OpenAPI document. Returns whether the number is a usable destination and
/// its canonical E.164 form.
/// </summary>
public class TwilioLookupsClient : IPhoneNumberValidator
{
    private readonly HttpClient _httpClient;

    public TwilioLookupsClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // The number is part of the request path. The HttpClient for this client has request logging
        // removed so the number never lands in a log via the client's URI.
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // Twilio returns 404 (code 20404) for a number it cannot resolve at all — treat that as "not a
        // usable destination" rather than a transport failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new List<string> { "NOT_FOUND" });
        }

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(cancellationToken: cancellationToken);
            }
            catch
            {
                // Non-JSON error body — fall back to status only.
            }

            throw new TwilioApiException(response.StatusCode, error?.Code, error?.Message, error?.MoreInfo);
        }

        var lookup = await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(cancellationToken: cancellationToken);
        if (lookup == null)
        {
            throw new TwilioApiException(response.StatusCode, null, "Twilio Lookups returned an empty response body.", null);
        }

        var validationErrors = lookup.ValidationErrors ?? new List<string>();
        return new PhoneNumberLookupResult(lookup.Valid, lookup.PhoneNumber, validationErrors);
    }
}
