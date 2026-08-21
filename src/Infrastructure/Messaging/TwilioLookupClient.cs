using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        ApplyAuthentication(_httpClient, _settings);
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new TwilioLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var pathNumber = Uri.EscapeDataString(phoneNumber.Trim());
        var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{pathNumber}");

        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup rejected a number with HTTP {StatusCode}", (int)response.StatusCode);
            var error = TryReadError(payload);
            throw new TwilioApiException((int)response.StatusCode, error?.Code?.ToString(),
                PhoneNumberSanitizer.Redact(error?.Message) ?? "The phone number could not be validated.");
        }

        var lookup = JsonSerializer.Deserialize<LookupJson>(payload, JsonOptions);
        if (lookup is null)
        {
            return new TwilioLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var errors = lookup.ValidationErrors ?? new List<string>();
        var canonical = lookup.Valid ? lookup.PhoneNumber : null;
        return new TwilioLookupResult(lookup.Valid, canonical, errors);
    }

    internal static void ApplyAuthentication(HttpClient httpClient, TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static TwilioErrorJson? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorJson>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class LookupJson
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    internal sealed class TwilioErrorJson
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
