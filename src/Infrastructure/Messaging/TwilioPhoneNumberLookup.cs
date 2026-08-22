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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioOptions> _options;

    public TwilioPhoneNumberLookup(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<LookedUpPhoneNumber> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }

        var encodedNumber = Uri.EscapeDataString(phoneNumber);
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{encodedNumber}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException((int)response.StatusCode, null, null);
        }

        var payload = JsonSerializer.Deserialize<LookupPayload>(json, JsonOptions) ?? new LookupPayload();
        return new LookedUpPhoneNumber
        {
            Valid = payload.Valid,
            PhoneNumber = payload.PhoneNumber,
            NationalFormat = payload.NationalFormat,
            CountryCode = payload.CountryCode,
            ValidationErrors = payload.ValidationErrors ?? new List<string>()
        };
    }

    private sealed class LookupPayload
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string? CountryCode { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
