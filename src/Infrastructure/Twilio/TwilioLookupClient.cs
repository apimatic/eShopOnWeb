using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : IPhoneNumberLookup
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _ = options;
    }

    public async Task<LookedUpPhoneNumber> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = "v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup returned {StatusCode}.", (int)response.StatusCode);
            return new LookedUpPhoneNumber(false, null, new[] { "lookup_failed" });
        }

        var dto = JsonSerializer.Deserialize<LookupResponseDto>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (dto == null)
        {
            return new LookedUpPhoneNumber(false, null, new[] { "lookup_failed" });
        }

        var errors = dto.ValidationErrors ?? new List<string>();
        return new LookedUpPhoneNumber(dto.Valid, dto.PhoneNumber, errors);
    }

    public static void ConfigureClient(HttpClient client, TwilioSettings settings)
    {
        client.BaseAddress = new Uri(LookupBaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
        TwilioMessagingClient.ApplyBasicAuth(client, settings);
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
