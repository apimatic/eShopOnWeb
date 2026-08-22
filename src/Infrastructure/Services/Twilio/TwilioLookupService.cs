using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupService : IPhoneNumberLookupService
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<TwilioLookupService> _logger;

    public TwilioLookupService(HttpClient httpClient, IOptions<TwilioSettings> options, IAppLogger<TwilioLookupService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var settings = options.Value;
        _httpClient.DefaultRequestHeaders.Authorization = CreateBasicAuth(settings.AccountSid, settings.AuthToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!phoneNumber.TrimStart().StartsWith('+') && !string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup returned HTTP {Status}.", (int)response.StatusCode);
            throw new InvalidPhoneNumberException("The provider could not validate the supplied number.");
        }

        TwilioLookupResource? payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<TwilioLookupResource>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Unable to parse Lookup response: {Message}", PhoneNumberLogSanitizer.Redact(ex.Message));
            throw new InvalidPhoneNumberException("The provider could not validate the supplied number.");
        }

        if (payload == null)
        {
            throw new InvalidPhoneNumberException("The provider could not validate the supplied number.");
        }

        var errors = payload.ValidationErrors?.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray()
                     ?? Array.Empty<string>();

        return new PhoneNumberLookupResult(payload.Valid, payload.PhoneNumber, payload.NationalFormat, errors);
    }

    private static AuthenticationHeaderValue CreateBasicAuth(string accountSid, string authToken)
    {
        var raw = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        return new AuthenticationHeaderValue("Basic", raw);
    }

    private sealed class TwilioLookupResource
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("national_format")]
        public string? NationalFormat { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
