using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupService : IPhoneNumberLookupService
{
    private const string LookupBaseUrl = "https://lookups.twilio.com/v2/PhoneNumbers/";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioLookupService> _logger;

    public TwilioLookupService(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioLookupService> logger)
    {
        _httpClient = httpClient;
        _settings = new TwilioSettings
        {
            AccountSid = options.Value.AccountSid?.Trim() ?? string.Empty,
            AuthToken = options.Value.AuthToken?.Trim() ?? string.Empty
        };
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, LookupBaseUrl + encoded);
        request.Headers.Authorization = CreateAuthHeader();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        TwilioLookupResponse? parsed = null;
        try
        {
            parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Lookup provider returned a payload that could not be parsed. HTTP {StatusCode}.", (int)response.StatusCode);
            throw new HttpRequestException("The messaging provider lookup response could not be parsed.");
        }

        if (parsed is null)
        {
            throw new HttpRequestException("The messaging provider lookup response was empty.");
        }

        var errors = parsed.ValidationErrors ?? new List<string>();
        return new PhoneNumberLookupResult(parsed.Valid, parsed.PhoneNumber, errors);
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private sealed class TwilioLookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
