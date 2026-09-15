using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Lookup v2 Basic Lookup: GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
/// Confirmed: https://www.twilio.com/docs/lookup/v2-api
/// This host is not governed by Twilio:BaseUrl (messaging API only).
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _httpClient.BaseAddress ??= new Uri("https://lookups.twilio.com/");
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = CreateBasicAuth();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Twilio Lookup request failed.");
            throw;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null);
        }

        var parsed = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (parsed is null || !parsed.Valid || string.IsNullOrWhiteSpace(parsed.PhoneNumber))
        {
            return new PhoneNumberLookupResult(false, null);
        }

        return new PhoneNumberLookupResult(true, parsed.PhoneNumber);
    }

    private AuthenticationHeaderValue CreateBasicAuth()
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
