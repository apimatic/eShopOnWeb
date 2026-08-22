using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }

        var path = "v2/PhoneNumbers/" + System.Net.WebUtility.UrlEncode(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioAuth.CreateHeader(_settings.AccountSid, _settings.AuthToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        if (!response.IsSuccessStatusCode)
        {
            var providerCode = await TryReadProviderCodeAsync(response, cancellationToken);
            _logger.LogWarning("Lookups API returned HTTP {StatusCode} (provider code {ProviderCode})", (int)response.StatusCode, providerCode);
            throw new TwilioClientException((int)response.StatusCode, providerCode);
        }

        var payload = await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(TwilioJson.Options, cancellationToken);
        if (payload is null)
        {
            throw new InvalidContactNumberException("The phone number is not a usable destination.");
        }

        return new PhoneNumberLookupResult
        {
            Valid = payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber),
            CanonicalPhoneNumber = payload.PhoneNumber
        };
    }

    private static async Task<int?> TryReadProviderCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioApiError>(TwilioJson.Options, cancellationToken);
            return error?.Code;
        }
        catch
        {
            return null;
        }
    }
}
