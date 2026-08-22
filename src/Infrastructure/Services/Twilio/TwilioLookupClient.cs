using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : IPhoneNumberLookupClient
{
    public const string HttpClientName = "TwilioLookup";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        IHttpClientFactory httpClientFactory,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var encoded = Uri.EscapeDataString(phoneNumber);
        var requestUri = $"v2/PhoneNumbers/{encoded}?Fields=line_type_intelligence";
        var response = await client.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup API request failed with status {Status}.", (int)response.StatusCode);
            throw new TwilioProviderException($"Lookup API request failed with status {(int)response.StatusCode}.");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<LookupPayload>(json, JsonOptions)
                      ?? throw new InvalidOperationException("The lookup provider returned an empty body.");

        var lineType = payload.LineTypeIntelligence?.Type;
        var lineTypeError = payload.LineTypeIntelligence?.ErrorCode;

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            lineTypeError is null ? lineType : null,
            lineTypeError,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    private sealed class LookupPayload
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string[]? ValidationErrors { get; set; }
        public LineTypePayload? LineTypeIntelligence { get; set; }
    }

    private sealed class LineTypePayload
    {
        public string? Type { get; set; }
        public int? ErrorCode { get; set; }
    }
}
