using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupHost = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        try
        {
            var pathNumber = Uri.EscapeDataString(phoneNumber);
            var url = $"{LookupHost}/v2/PhoneNumbers/{pathNumber}?Fields={Uri.EscapeDataString("line_type_intelligence")}";
            if (!string.IsNullOrWhiteSpace(countryCode) && !phoneNumber.TrimStart().StartsWith('+'))
            {
                url += $"&CountryCode={Uri.EscapeDataString(countryCode.Trim())}";
            }

            using var response = await TwilioHttpRetry.SendAsync(_httpClient, () =>
            {
                var retryRequest = new HttpRequestMessage(HttpMethod.Get, url);
                ApplyBasicAuth(retryRequest);
                return retryRequest;
            }, isIdempotent: true, cancellationToken);

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioApiException.FromResponse((int)response.StatusCode, payload);
            }

            var parsed = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions)
                         ?? throw new TwilioApiException((int)response.StatusCode, null, "Lookup returned an empty body.");

            return new PhoneNumberLookupResult
            {
                Valid = parsed.Valid,
                CanonicalPhoneNumber = parsed.PhoneNumber,
                NationalFormat = parsed.NationalFormat,
                CountryCode = parsed.CountryCode,
                ValidationErrors = parsed.ValidationErrors ?? new List<string>(),
                LineType = parsed.LineTypeIntelligence?.Type,
                LineTypeErrorCode = parsed.LineTypeIntelligence?.ErrorCode
            };
        }
        catch (TwilioApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TwilioApiException(0, null, PhoneNumberRedactor.Redact(ex.Message));
        }
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string? CountryCode { get; set; }
        public List<string>? ValidationErrors { get; set; }
        public LineTypeIntelligenceDto? LineTypeIntelligence { get; set; }
    }

    private sealed class LineTypeIntelligenceDto
    {
        public string? Type { get; set; }
        public int? ErrorCode { get; set; }
    }
}
