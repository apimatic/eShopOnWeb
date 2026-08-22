using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Hand-written client for Twilio Lookups v2 FetchPhoneNumber
/// as specified in api-specs/twilio/twilio_lookups_v2.
/// Twilio:BaseUrl does not apply — lookups are served from a different host.
/// </summary>
public class TwilioLookupClient : IPhoneNumberLookupClient
{
    public const string HttpClientName = "TwilioLookups";
    public const string DefaultBaseUrl = "https://lookups.twilio.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        TwilioSmsGateway.ApplyAuthentication(_httpClient, options.Value);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = "v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var response = await _httpClient.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            int? providerCode = null;
            try
            {
                var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
                providerCode = JsonSerializer.Deserialize<TwilioErrorResponse>(errorPayload, JsonOptions)?.Code;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Twilio Lookup error response could not be parsed. HTTP status {StatusCode}.", (int)response.StatusCode);
            }

            _logger.LogWarning("Twilio Lookup failed with HTTP {StatusCode} and provider code {ProviderCode}.", (int)response.StatusCode, providerCode);
            throw new PhoneNumberLookupException("The provider could not look up the phone number.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions);
        if (lookup is null)
        {
            throw new PhoneNumberLookupException("The provider returned an empty lookup response.");
        }

        return new PhoneNumberLookupResult
        {
            IsValid = lookup.Valid,
            CanonicalNumber = lookup.PhoneNumber,
            ValidationErrors = (IReadOnlyList<string>?)lookup.ValidationErrors ?? Array.Empty<string>()
        };
    }
}
