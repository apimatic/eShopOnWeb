using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _httpClient.BaseAddress = new Uri(LookupBaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/PhoneNumbers/{encoded}");
        ApplyBasicAuth(request);

        using var response = await TwilioHttpRetry.SendAsync(_httpClient, request, retryOnServerError: true, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw TwilioErrorParser.ToException(response.StatusCode, payload, "Phone number lookup failed.");
        }

        var parsed = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (parsed is null)
        {
            return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new[] { "NOT_A_NUMBER" } };
        }

        return new PhoneNumberLookupResult
        {
            Valid = parsed.Valid,
            CanonicalPhoneNumber = parsed.PhoneNumber,
            NationalFormat = parsed.NationalFormat,
            CountryCode = parsed.CountryCode,
            ValidationErrors = parsed.ValidationErrors ?? new List<string>()
        };
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string? CountryCode { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
