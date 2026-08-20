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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookup";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var builder = new UriBuilder(LookupBaseUrl)
        {
            Path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}"
        };

        var query = new List<string> { "Fields=line_type_intelligence" };
        if (!string.IsNullOrWhiteSpace(countryCode) && !phoneNumber.TrimStart().StartsWith('+'))
        {
            query.Add($"CountryCode={Uri.EscapeDataString(countryCode)}");
        }

        builder.Query = string.Join("&", query);

        using var request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<TwilioErrorResource>(json);
            _logger.LogWarning("Twilio Lookup failed with HTTP {Status} and error code {ErrorCode}.", (int)response.StatusCode, error?.Code);
            throw new TwilioRequestException((int)response.StatusCode, error?.Code, "Twilio Lookup request failed.");
        }

        var payload = JsonSerializer.Deserialize<LookupResponse>(json, JsonOptions) ?? new LookupResponse();
        return new PhoneNumberLookupResult
        {
            Valid = payload.Valid,
            PhoneNumber = payload.PhoneNumber,
            NationalFormat = payload.NationalFormat,
            ValidationErrors = payload.ValidationErrors ?? new List<string>(),
            LineType = payload.LineTypeIntelligence?.Type,
            LineTypeErrorCode = payload.LineTypeIntelligence?.ErrorCode
        };
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }

        [JsonPropertyName("line_type_intelligence")]
        public LineTypeIntelligenceResource? LineTypeIntelligence { get; set; }
    }

    private sealed class LineTypeIntelligenceResource
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class TwilioErrorResource
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
