using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookup";

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

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var encoded = Uri.EscapeDataString(phoneNumber);
        var uri = new Uri($"https://lookups.twilio.com/v2/PhoneNumbers/{encoded}");

        using var response = await TwilioHttp.SendWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = CreateAuthHeader();
                return request;
            },
            retryServerErrors: true,
            _logger,
            "Lookup",
            cancellationToken);

        await TwilioHttp.EnsureSuccessAsync(response, "Lookup");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = TwilioHttp.Deserialize<LookupResponse>(json);

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    private HttpClient CreateClient()
    {
        return _httpClientFactory.CreateClient(HttpClientName);
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var raw = $"{_options.AccountSid}:{_options.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }
}
