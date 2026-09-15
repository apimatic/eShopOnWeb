using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : IPhoneNumberLookupClient
{
    public const string HttpClientName = "TwilioLookups";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(IHttpClientFactory httpClientFactory, IOptions<TwilioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyBasicAuth(request);

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToException(response, payload);
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions)
            ?? new TwilioLookupResponse();

        return new PhoneLookupResult(
            lookup.Valid,
            lookup.PhoneNumber,
            lookup.CountryCode,
            lookup.ValidationErrors ?? (IReadOnlyList<string>)Array.Empty<string>());
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }
}
