using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri("https://lookups.twilio.com/");
        }

        TwilioHttp.ApplyAuth(_httpClient, options.Value);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            _logger.LogWarning("Phone lookup failed with HTTP {StatusCode} code {ErrorCode}.", (int)response.StatusCode, error?.Code);
            return new PhoneLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, TwilioHttp.JsonOptions);
        if (lookup == null)
        {
            return new PhoneLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var errors = lookup.ValidationErrors ?? new List<string>();
        return new PhoneLookupResult(lookup.Valid, lookup.PhoneNumber, errors);
    }

    private static TwilioErrorPayload? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorPayload>(payload, TwilioHttp.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
