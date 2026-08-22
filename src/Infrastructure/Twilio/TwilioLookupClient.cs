using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Lookups v2 client for GET /v2/PhoneNumbers/{PhoneNumber} as defined in api-specs/twilio/twilio_lookups_v2.
/// Uses lookups.twilio.com; Twilio:BaseUrl does not apply.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    public const string HttpClientName = "TwilioLookups";
    private const string DefaultBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(DefaultBaseUrl + "/", UriKind.Absolute);
        }
    }

    public async Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new TwilioLookupResult { Valid = false, ValidationErrors = new[] { "NOT_A_NUMBER" } };
        }

        EnsureCredentials();

        // Spec: path parameter PhoneNumber; E.164 '+' must be percent-encoded.
        var encoded = Uri.EscapeDataString(phoneNumber.Trim());
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/PhoneNumbers/{encoded}");
        ApplyBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new TwilioLookupResult { Valid = false, ValidationErrors = new[] { "NOT_A_NUMBER" } };
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException($"Lookups API FetchPhoneNumber failed with status {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<LookupResponseDto>(stream, _jsonOptions, cancellationToken);

        if (payload == null)
        {
            throw new TwilioApiException("Lookups API FetchPhoneNumber returned an empty body.");
        }

        return new TwilioLookupResult
        {
            Valid = payload.Valid,
            PhoneNumber = payload.PhoneNumber,
            ValidationErrors = payload.ValidationErrors ?? Array.Empty<string>()
        };
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new TwilioApiException("Twilio AccountSid and AuthToken are not configured.");
        }
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
