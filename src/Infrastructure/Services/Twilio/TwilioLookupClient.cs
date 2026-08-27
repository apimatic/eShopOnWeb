using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private const string LookupHost = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var encodedNumber = Uri.EscapeDataString(phoneNumber);
        var uri = $"{LookupHost}/v2/PhoneNumbers/{encodedNumber}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            uri += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
                request.Headers.Accept.ParseAdd("application/json");
                return request;
            },
            retryServerErrors: true,
            cancellationToken);

        await TwilioHttp.EnsureSuccessAsync(response);
        var dto = await TwilioHttp.ReadJsonAsync<TwilioLookupDto>(response);

        return new PhoneNumberLookupResult
        {
            Valid = dto.Valid,
            CanonicalPhoneNumber = dto.PhoneNumber,
            NationalFormat = dto.NationalFormat,
            CountryCode = dto.CountryCode,
            ValidationErrors = dto.ValidationErrors ?? new System.Collections.Generic.List<string>()
        };
    }
}
