using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioSettings> _options;
    private readonly IAppLogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        try
        {
            TwilioHttp.EnsureConfigured(settings);
        }
        catch (TwilioMessagingException)
        {
            _logger.LogWarning("Twilio lookup skipped because credentials are not configured.");
            return new PhoneNumberLookupResult { Succeeded = false };
        }

        // Lookup is served from lookups.twilio.com and is not governed by Twilio:BaseUrl.
        var encoded = System.Uri.EscapeDataString(phoneNumber);
        var url = $"https://lookups.twilio.com/v2/PhoneNumbers/{encoded}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        TwilioHttp.ApplyBasicAuth(request, settings);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Twilio Lookup request failed to complete.");
            return new PhoneNumberLookupResult { Succeeded = false };
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio Lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
                return new PhoneNumberLookupResult { Succeeded = false };
            }

            TwilioLookupResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions);
            }
            catch (JsonException)
            {
                _logger.LogWarning("Twilio Lookup returned an unreadable payload.");
                return new PhoneNumberLookupResult { Succeeded = false };
            }

            if (parsed == null)
            {
                return new PhoneNumberLookupResult { Succeeded = false };
            }

            return new PhoneNumberLookupResult
            {
                Succeeded = true,
                Valid = parsed.Valid,
                CanonicalPhoneNumber = parsed.PhoneNumber,
                ValidationErrors = parsed.ValidationErrors ?? []
            };
        }
    }
}
