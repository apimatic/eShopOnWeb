using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Phone number validation via the Twilio Lookups API
/// (api-specs/twilio/twilio_lookups_v2: GET /v2/PhoneNumbers/{PhoneNumber}).
/// Lookups is served from lookups.twilio.com and is not governed by
/// Twilio:BaseUrl, which applies to the messaging API only.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioPhoneNumberValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.Value.AccountSid}:{settings.Value.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<string?> ValidateAndNormalizeAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Lookups call failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new SmsProviderException($"Twilio Lookups call failed with HTTP {(int)response.StatusCode}.");
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupPhoneNumberResource>(payload, JsonOptions);
        if (lookup == null || !lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            return null;
        }

        // Store the provider's canonical (E.164) form, not what the caller typed.
        return lookup.PhoneNumber;
    }
}
