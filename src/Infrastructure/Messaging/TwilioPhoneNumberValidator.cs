using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Phone number validation via the Twilio Lookups API, built against
/// api-specs/twilio/twilio_lookups_v2 (GET /v2/PhoneNumbers/{PhoneNumber}).
/// The Lookups API is served from its own host and is not governed by the
/// messaging BaseUrl override.
/// </summary>
public class TwilioPhoneNumberValidator : IPhoneNumberValidator
{
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;

    public TwilioPhoneNumberValidator(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(TwilioSmsGateway.HttpClientName);
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        var response = await client.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(response.StatusCode, null, "FetchPhoneNumber");
        }

        var lookup = (await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(cancellationToken: cancellationToken))!;
        return new PhoneNumberValidationResult(
            lookup.Valid,
            lookup.Valid ? lookup.PhoneNumber : null,
            lookup.ValidationErrors ?? new System.Collections.Generic.List<string>());
    }
}
