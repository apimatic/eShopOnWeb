using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : IPhoneNumberLookupService
{
    private static readonly Uri LookupHost = new("https://lookups.twilio.com");

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
        _httpClient.BaseAddress ??= LookupHost;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new ProviderUnavailableException("Twilio AccountSid and AuthToken are not configured.");
        }

        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(LookupHost, path));
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup request failed. StatusCode={StatusCode}", (int)response.StatusCode);
            if ((int)response.StatusCode >= 500)
            {
                throw new ProviderUnavailableException("The phone number lookup service is unavailable.");
            }

            return new PhoneNumberLookupResult(false, null);
        }

        var dto = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions);
        if (dto is null)
        {
            throw new ProviderUnavailableException("The phone number lookup service returned an empty response.");
        }

        return new PhoneNumberLookupResult(
            dto.Valid,
            string.IsNullOrWhiteSpace(dto.PhoneNumber) ? null : dto.PhoneNumber,
            dto.ValidationErrors);
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }
}
