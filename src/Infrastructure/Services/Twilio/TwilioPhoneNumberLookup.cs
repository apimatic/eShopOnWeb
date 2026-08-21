using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioPhoneNumberLookup : IPhoneNumberLookup
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioSettings> _settings;

    public TwilioPhoneNumberLookup(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(TwilioHttp.LookupsClientName);
        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        TwilioHttp.ApplyBasicAuth(request, _settings);

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            var code = error?.Code?.ToString() ?? ((int)response.StatusCode).ToString();
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_FAILED_" + code });
        }

        var body = JsonSerializer.Deserialize<TwilioLookupResponse>(payload, JsonOptions);
        if (body is null)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_UNREADABLE" });
        }

        return new PhoneNumberLookupResult(
            body.Valid,
            body.Valid ? body.PhoneNumber : null,
            body.ValidationErrors ?? Array.Empty<string>());
    }

    private static TwilioRestError? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioRestError>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
