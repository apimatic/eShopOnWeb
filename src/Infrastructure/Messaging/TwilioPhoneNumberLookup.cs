using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioPhoneNumberLookup> _logger;

    public TwilioPhoneNumberLookup(HttpClient httpClient, ILogger<TwilioPhoneNumberLookup> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += "?CountryCode=" + Uri.EscapeDataString(countryCode);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Phone number lookup could not be completed");
            throw new InvalidOperationException("The provider could not look up the phone number.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number lookup returned HTTP {StatusCode}", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, null, new[] { "LOOKUP_FAILED" });
        }

        var payload = TwilioJson.Deserialize<TwilioLookupResponse>(body);
        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }
}
