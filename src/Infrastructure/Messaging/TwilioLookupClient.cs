using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio Lookup returned {StatusCode}: {Message}",
                (int)response.StatusCode,
                LogSanitizer.RedactPhoneNumbers(payload));
            return new TwilioLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var document = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (document == null)
        {
            return new TwilioLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        return new TwilioLookupResult(
            document.Valid,
            document.Valid ? document.PhoneNumber : null,
            document.ValidationErrors);
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string?[]? ValidationErrors { get; set; }
    }
}
