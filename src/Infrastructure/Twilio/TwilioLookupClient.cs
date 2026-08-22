using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/PhoneNumbers/{encoded}");
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_settings.AccountSid, _settings.AuthToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new TwilioApiException(TwilioHttp.FormatError((int)response.StatusCode, payload));
        }

        var dto = JsonSerializer.Deserialize<LookupResponseDto>(payload, TwilioHttp.JsonOptions)
            ?? throw new TwilioApiException("Twilio Lookup returned an empty response.");

        return new TwilioLookupResult(
            dto.Valid,
            dto.PhoneNumber,
            dto.ValidationErrors ?? new List<string>());
    }

    private sealed class LookupResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
