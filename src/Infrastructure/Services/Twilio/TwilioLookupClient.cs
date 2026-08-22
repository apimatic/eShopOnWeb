using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Lookups v2 FetchPhoneNumber — GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
/// as specified in twilio_lookups_v2.yaml. Not governed by Twilio:BaseUrl.
/// </summary>
public sealed class TwilioLookupClient : IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookups";
    internal const string DefaultBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public TwilioLookupClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await client.GetAsync(path, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToApiException((int)response.StatusCode, payload);
        }

        var parsed = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions)
            ?? throw new TwilioApiException((int)response.StatusCode, null, null);

        return new PhoneNumberLookupResult(
            parsed.Valid,
            parsed.PhoneNumber,
            (IReadOnlyList<string>)(parsed.ValidationErrors ?? new List<string>()));
    }
}
