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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Validates and canonicalises a number with Twilio's Lookup v2 API. Lookup is served from a
/// different host than messaging and is deliberately NOT governed by the <c>Twilio:BaseUrl</c>
/// override (that override is for the messaging API only).
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly IAppLogger<TwilioPhoneNumberValidator> _logger;

    public TwilioPhoneNumberValidator(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioPhoneNumberValidator> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var o = options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{o.AccountSid}:{o.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider could not parse the number as a possible number at all.
            return new PhoneNumberValidation(false, null, new List<string> { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
        {
            // Do not treat a provider outage as "invalid" - it is a different failure. The message
            // deliberately omits the number.
            _logger.LogWarning("Lookup returned HTTP {Status} while validating a contact number.", (int)response.StatusCode);
            throw new TwilioApiException((int)response.StatusCode, null,
                $"Twilio lookup API error (HTTP {(int)response.StatusCode}).");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl)
                    && validEl.ValueKind == JsonValueKind.True;

        var canonical = root.TryGetProperty("phone_number", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString()
            : null;

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errsEl) && errsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errsEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);
            }
        }

        if (!valid || string.IsNullOrEmpty(canonical))
            return new PhoneNumberValidation(false, null, errors);

        return new PhoneNumberValidation(true, canonical, errors);
    }
}
