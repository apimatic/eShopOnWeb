using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Lookups v2 FetchPhoneNumber — GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
/// Twilio:BaseUrl does not apply; lookups are served from a different host.
/// </summary>
public class TwilioLookupClient : TwilioApiClientBase, IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookups";
    public const string DefaultBaseUrl = "https://lookups.twilio.com/";

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
        : base(httpClient, options)
    {
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await HttpClient.GetAsync(path, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var payload = JsonSerializer.Deserialize<LookupResponse>(json, JsonOptions)
            ?? throw new TwilioApiException(response.StatusCode, null, "Lookups returned an empty body.");

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.CountryCode,
            payload.ValidationErrors ?? new List<string>());
    }
}
