using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio Lookups v2 API, built to the OpenAPI spec in
/// <c>api-specs/twilio/twilio_lookups_v2</c>. Validates a number and returns the provider's canonical
/// E.164 form. Lookups is served from its own host (<c>lookups.twilio.com</c>) and is deliberately NOT
/// governed by <c>Twilio:BaseUrl</c>, which overrides only the messaging API.
/// </summary>
public class TwilioLookupClient : IPhoneNumberLookup
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public TwilioLookupClient(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        var settings = options.Value;
        _http.DefaultRequestHeaders.Authorization =
            TwilioMessagingClient.BuildBasicAuth(settings.AccountSid, settings.AuthToken);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(url, cancellationToken);

        // A number the provider does not recognise at all comes back as 404 — treat as not a usable
        // destination rather than a transport failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new List<string> { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new SmsGatewayException(
                $"Twilio Lookups call failed with HTTP {(int)response.StatusCode}.");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<TwilioLookupDto>(payload, JsonOptions);
        if (dto is null)
        {
            throw new SmsGatewayException("Twilio Lookups returned an empty body.");
        }

        var validationErrors = (IReadOnlyList<string>?)dto.ValidationErrors ?? Array.Empty<string>();
        // Only trust the canonical form when the provider considers the number valid.
        var canonical = dto.Valid ? dto.PhoneNumber : null;
        return new PhoneNumberLookupResult(dto.Valid, canonical, validationErrors);
    }
}
