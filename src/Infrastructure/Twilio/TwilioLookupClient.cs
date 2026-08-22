using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Lookups v2 FetchPhoneNumber client as defined by api-specs/twilio/twilio_lookups_v2.
/// Twilio:BaseUrl does not apply; lookups are served from lookups.twilio.com.
/// </summary>
public class TwilioLookupClient : IPhoneNumberLookupService
{
    public const string HttpClientName = "TwilioLookups";
    public const string DefaultBaseUrl = "https://lookups.twilio.com/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        _logger.LogInformation("Twilio FetchPhoneNumber returned {StatusCode}.", (int)response.StatusCode);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new[] { "NOT_FOUND" } };
        }

        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorDto? error = null;
            try
            {
                error = JsonSerializer.Deserialize<TwilioErrorDto>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                // ignore malformed error payload
            }

            throw new SmsGatewayException(
                PhoneNumberLogRedactor.Redact(error?.Message) is { Length: > 0 } redacted
                    ? $"FetchPhoneNumber failed: {redacted}"
                    : $"FetchPhoneNumber failed with HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                error?.Code);
        }

        TwilioLookupDto? lookup = null;
        try
        {
            lookup = JsonSerializer.Deserialize<TwilioLookupDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new[] { "UNREADABLE_RESPONSE" } };
        }

        if (lookup is null)
        {
            return new PhoneNumberLookupResult { Valid = false };
        }

        return new PhoneNumberLookupResult
        {
            Valid = lookup.Valid,
            CanonicalPhoneNumber = lookup.PhoneNumber,
            ValidationErrors = lookup.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>()
        };
    }
}
