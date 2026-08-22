using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioHttp.CreateAuthHeader(_settings);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup failed with status {StatusCode}.", (int)response.StatusCode);
            if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
            {
                return new PhoneNumberLookup { Valid = false };
            }

            throw new HttpRequestException($"Twilio Lookup failed with status {(int)response.StatusCode}.");
        }

        var parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, TwilioHttp.JsonOptions);
        if (parsed is null)
        {
            throw new InvalidOperationException("Twilio Lookup returned an empty response.");
        }

        return new PhoneNumberLookup
        {
            Valid = parsed.Valid,
            CanonicalPhoneNumber = parsed.PhoneNumber,
            ValidationErrors = parsed.ValidationErrors ?? Array.Empty<string>()
        };
    }

    private sealed class TwilioLookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
