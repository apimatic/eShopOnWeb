using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioHttp.BasicAuth(_settings.AccountSid, _settings.AuthToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Twilio Lookup request failed to complete.");
            throw;
        }

        using (response)
        {
            if (response.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError
                && response.StatusCode is not System.Net.HttpStatusCode.Unauthorized and not System.Net.HttpStatusCode.Forbidden)
            {
                return new PhoneNumberLookupResult(false, null);
            }

            await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, TwilioHttp.JsonOptions)
                ?? throw new TwilioApiException((int)response.StatusCode, null);

            return new PhoneNumberLookupResult(parsed.Valid, parsed.PhoneNumber);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }
    }
}
