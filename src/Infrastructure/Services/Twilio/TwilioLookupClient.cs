using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly AuthenticationHeaderValue _authorization;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _authorization = TwilioHttp.CreateBasicAuth(_settings.AccountSid, _settings.AuthToken);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        var url = new Uri(new Uri(LookupBaseUrl), path);
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            _authorization,
            allowRetryOnServerError: true,
            cancellationToken);

        await TwilioHttp.EnsureSuccessAsync(response, "Phone number lookup");
        var payload = await TwilioHttp.ReadJsonAsync<TwilioLookupResponse>(response);

        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            (IReadOnlyList<string>)(payload.ValidationErrors ?? new List<string>()));
    }
}
