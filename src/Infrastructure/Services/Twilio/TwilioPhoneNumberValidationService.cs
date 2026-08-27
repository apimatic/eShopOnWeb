using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates phone numbers through the Twilio Lookup API (hosted at lookups.twilio.com —
/// a separate Twilio capability, not governed by the messaging BaseUrl override).
/// Never logs the phone number being validated.
/// </summary>
public class TwilioPhoneNumberValidationService : IPhoneNumberValidationService
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioPhoneNumberValidationService> _logger;

    public TwilioPhoneNumberValidationService(HttpClient httpClient, IOptions<TwilioSettings> settings,
        ILogger<TwilioPhoneNumberValidationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{settings.Value.AccountSid}:{settings.Value.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberValidationResult(false, null, "A phone number is required.");
        }

        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number lookup returned HTTP {StatusCode}", (int)response.StatusCode);
            return new PhoneNumberValidationResult(false, null,
                $"The provider could not evaluate the number (HTTP {(int)response.StatusCode}).");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var valid = json.RootElement.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var canonical = json.RootElement.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
        {
            var errors = json.RootElement.TryGetProperty("validation_errors", out var ve) && ve.ValueKind == JsonValueKind.Array
                ? string.Join(", ", ve.EnumerateArray().Select(e => e.GetString()))
                : "not a usable destination";
            return new PhoneNumberValidationResult(false, null, $"The provider does not consider this a usable destination ({errors}).");
        }

        return new PhoneNumberValidationResult(true, canonical, null);
    }
}
