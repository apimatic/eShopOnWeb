using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : IPhoneNumberLookupService
{
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<LookedUpPhoneNumber> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new TwilioUnavailableException("Twilio lookup is not configured.");
        }

        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        HttpResponseMessage response;
        try
        {
            response = await SendWithRetryAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Twilio Lookup request failed to complete.");
            throw new TwilioUnavailableException("The phone number lookup service could not be reached.", ex);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            throw new TwilioUnavailableException($"Phone number lookup failed (HTTP {(int)response.StatusCode}).");
        }

        var dto = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions) ?? new LookupResponse();
        return new LookedUpPhoneNumber(
            dto.Valid,
            dto.PhoneNumber,
            dto.NationalFormat,
            dto.ValidationErrors ?? Array.Empty<string>());
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(HttpRequestMessage template, CancellationToken cancellationToken)
    {
        HttpResponseMessage? lastResponse = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var request = new HttpRequestMessage(template.Method, template.RequestUri);
            foreach (var header in template.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            lastResponse = await _httpClient.SendAsync(request, cancellationToken);
            var status = (int)lastResponse.StatusCode;
            if (attempt < 2 && (status == 429 || status >= 500))
            {
                lastResponse.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), cancellationToken);
                continue;
            }

            return lastResponse;
        }

        return lastResponse!;
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
