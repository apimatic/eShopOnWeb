using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookups";
    private const string DefaultBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientName);
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!_settings.HasCredentials)
        {
            _logger.LogWarning("Twilio lookup was requested but credentials are not configured.");
            return new PhoneNumberLookupResult(false, null);
        }

        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(DefaultBaseUrl, path));
        ApplyBasicAuth(request);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Twilio Lookup rejected a phone number with HTTP {StatusCode}.",
                    (int)response.StatusCode);
                return new PhoneNumberLookupResult(false, null);
            }

            var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions);
            if (lookup is null || !lookup.Valid || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new PhoneNumberLookupResult(false, lookup?.PhoneNumber);
            }

            return new PhoneNumberLookupResult(true, lookup.PhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Twilio Lookup failed: {Message}", PiiRedactor.Redact(ex.Message));
            return new PhoneNumberLookupResult(false, null);
        }
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static Uri Combine(string baseUrl, string path)
    {
        return new Uri($"{baseUrl.TrimEnd('/')}{path}");
    }
}
