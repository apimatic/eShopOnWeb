using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for Twilio Lookups v2 (twilio_lookups_v2 FetchPhoneNumber).
/// Hosted at lookups.twilio.com; Twilio:BaseUrl does not apply.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio FetchPhoneNumber failed with HTTP {StatusCode}.", (int)response.StatusCode);
            if ((int)response.StatusCode == 404)
            {
                return new PhoneNumberLookupResult
                {
                    Valid = false,
                    CanonicalNumber = null,
                    ValidationErrors = new[] { "NOT_FOUND" }
                };
            }

            await TwilioHttp.ThrowForErrorAsync(response, "FetchPhoneNumber", cancellationToken);
        }

        var lookup = await TwilioHttp.ReadJsonAsync<TwilioLookupResponse>(response, cancellationToken);
        return new PhoneNumberLookupResult
        {
            Valid = lookup.Valid,
            CanonicalNumber = lookup.PhoneNumber,
            ValidationErrors = lookup.ValidationErrors ?? new List<string>()
        };
    }
}
