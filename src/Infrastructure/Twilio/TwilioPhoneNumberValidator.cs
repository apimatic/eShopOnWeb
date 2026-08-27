using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio Lookup v2 implementation of <see cref="IPhoneNumberValidator"/>.
/// Contract verified against https://www.twilio.com/docs/lookup/v2-api.
/// The Lookup API is served from lookups.twilio.com and is NOT governed by
/// Twilio:BaseUrl (which only overrides the messaging API).
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;

        var accountSid = settings.Value.AccountSid;
        var authToken = settings.Value.AuthToken;
        if (string.IsNullOrWhiteSpace(accountSid) || string.IsNullOrWhiteSpace(authToken))
        {
            throw new InvalidOperationException("Twilio settings are missing: Twilio:AccountSid and Twilio:AuthToken must be configured (e.g. via user-secrets).");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{accountSid}:{authToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var uri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MessageProviderException($"Twilio Lookup request failed with status {(int)response.StatusCode}.");
        }

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var isValid = root.TryGetProperty("valid", out var validElement) && validElement.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var numberElement) && numberElement.ValueKind == JsonValueKind.String
            ? numberElement.GetString()
            : null;

        IReadOnlyList<string> errors = Array.Empty<string>();
        if (root.TryGetProperty("validation_errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            errors = errorsElement.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        return new PhoneNumberValidationResult(isValid, isValid ? canonical : null, errors);
    }
}
