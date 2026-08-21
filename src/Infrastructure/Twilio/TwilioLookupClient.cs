using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : IPhoneNumberLookupClient
{
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var requestUri = new Uri(LookupBaseUri, $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        ApplyBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup request failed with HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null);
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (lookup is null)
        {
            return new PhoneNumberLookupResult(false, null);
        }

        return new PhoneNumberLookupResult(lookup.Valid, lookup.Valid ? lookup.PhoneNumber : null);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_options.AccountSid}:{_options.AuthToken}";
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }
}
