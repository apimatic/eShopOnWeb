using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio Lookups v2 API, built to the OpenAPI contract in
/// <c>api-specs/twilio/twilio_lookups_v2</c>. Validates a number and returns the provider's canonical
/// E.164 form. This capability is served from <c>lookups.twilio.com</c> and is deliberately not
/// governed by the <c>Twilio:BaseUrl</c> messaging override. The number is never logged.
/// </summary>
public class TwilioLookupClient : IPhoneNumberValidator
{
    private const string PhoneNumberPathFormat = "/v2/PhoneNumbers/{0}";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, TwilioSettings settings,
        IAppLogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(TwilioSettings.LookupsBaseUrl);
        if (_settings.HasCredentials)
        {
            var raw = Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        }
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!_settings.HasCredentials)
            throw new InvalidOperationException(
                "Twilio is not configured. Set Twilio:AccountSid and Twilio:AuthToken (via user-secrets).");

        var path = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            PhoneNumberPathFormat, Uri.EscapeDataString(phoneNumber));

        using var response = await _httpClient.GetAsync(path, cancellationToken);

        // A number the provider cannot even parse is not a usable destination — reject it here.
        if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.BadRequest)
        {
            _logger.LogInformation($"Phone number lookup rejected the number (HTTP {(int)response.StatusCode}).");
            return new PhoneNumberValidationResult { IsValid = false, Errors = new[] { "NOT_A_NUMBER" } };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw BuildException(response.StatusCode, body);

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(body);
        if (lookup is null)
            throw new TwilioApiException(response.StatusCode, null, "Twilio returned an unrecognized lookup payload.", null);

        if (!lookup.Valid)
        {
            _logger.LogInformation("Phone number lookup returned not-valid.");
            return new PhoneNumberValidationResult
            {
                IsValid = false,
                Errors = lookup.ValidationErrors ?? Array.Empty<string>()
            };
        }

        return new PhoneNumberValidationResult
        {
            IsValid = true,
            CanonicalNumber = lookup.PhoneNumber
        };
    }

    private static TwilioApiException BuildException(HttpStatusCode status, string body)
    {
        TwilioErrorResponse? error = null;
        try { error = JsonSerializer.Deserialize<TwilioErrorResponse>(body); }
        catch (JsonException) { /* non-JSON error body */ }
        return new TwilioApiException(status, error?.Code, error?.Message, error?.MoreInfo);
    }
}
