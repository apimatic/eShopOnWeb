using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Lookups v2 FetchPhoneNumber — GET /v2/PhoneNumbers/{PhoneNumber} on lookups.twilio.com.
/// Twilio:BaseUrl does not apply; lookups are a different host than the messaging API.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupBaseUrl = "https://lookups.twilio.com/";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress ??= new Uri(LookupBaseUrl);
        _httpClient.DefaultRequestHeaders.Authorization =
            TwilioJson.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
    }

    public async Task<LookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidContactNumberException();
        }

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioResponseGuard.ThrowIfLookupFailed((int)response.StatusCode, content);

        var body = TwilioJson.Read<TwilioLookupResponseBody>(content);
        return new LookupResult(body.Valid, body.PhoneNumber);
    }
}
