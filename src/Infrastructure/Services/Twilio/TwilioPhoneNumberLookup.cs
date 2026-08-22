using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookups";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioPhoneNumberLookup> _logger;

    public TwilioPhoneNumberLookup(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> options,
        ILogger<TwilioPhoneNumberLookup> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = CreateAuthHeader();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, null, "NOT_FOUND");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number lookup failed with status {StatusCode}.", (int)response.StatusCode);
            throw new TwilioApiException((int)response.StatusCode, "The messaging provider could not look up the phone number.");
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupDto>(payload, JsonOptions);
        if (lookup == null)
        {
            return new PhoneNumberLookupResult(false, null, null, "UNPARSEABLE");
        }

        var reason = lookup.ValidationErrors is { Count: > 0 }
            ? string.Join(",", lookup.ValidationErrors)
            : null;

        return new PhoneNumberLookupResult(lookup.Valid, lookup.PhoneNumber, lookup.CountryCode, reason);
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }
}
