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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var encoded = Uri.EscapeDataString(phoneNumber.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://lookups.twilio.com/v2/PhoneNumbers/{encoded}");
        AddBasicAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Lookup request to the provider failed.");
            throw new InvalidOperationException("The phone number lookup service is unavailable.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        LookupResponse? lookup;
        try
        {
            lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Lookup returned a payload that could not be parsed. HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        if (lookup is null)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var errors = lookup.ValidationErrors ?? Array.Empty<string>();
        var valid = lookup.Valid && !string.IsNullOrWhiteSpace(lookup.PhoneNumber);
        return new PhoneNumberLookupResult(valid, lookup.PhoneNumber, errors);
    }

    private void AddBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }
}
