using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookup";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TwilioPhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));

        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup API returned {StatusCode}", (int)response.StatusCode);
            return new TwilioPhoneLookupResult { Valid = false };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<LookupPayload>(stream, JsonOptions, cancellationToken);
        if (payload is null)
        {
            return new TwilioPhoneLookupResult { Valid = false };
        }

        return new TwilioPhoneLookupResult
        {
            Valid = payload.Valid,
            CanonicalNumber = payload.PhoneNumber,
            ValidationErrors = payload.ValidationErrors ?? Array.Empty<string>()
        };
    }

    private sealed class LookupPayload
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }
}
