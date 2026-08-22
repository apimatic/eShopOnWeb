using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSettings = Microsoft.eShopWeb.TwilioSettings;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
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

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookup returned {StatusCode}", (int)response.StatusCode);
            return new PhoneNumberLookupResult { Valid = false, CanonicalPhoneNumber = null };
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        var errors = lookup?.ValidationErrors ?? new List<string>();
        var valid = lookup?.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber);

        _logger.LogInformation("Twilio Lookup completed. Valid={Valid}", valid);

        return new PhoneNumberLookupResult
        {
            Valid = valid,
            CanonicalPhoneNumber = lookup?.PhoneNumber,
            ValidationErrors = errors
        };
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
