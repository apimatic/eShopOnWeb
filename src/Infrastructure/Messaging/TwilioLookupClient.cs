using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TwilioHttp.LookupUrl(phoneNumber));
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioRequestException((int)response.StatusCode, TryReadError(payload)?.Code);
        }

        var lookup = JsonSerializer.Deserialize<TwilioLookupJson>(payload, JsonOptions)
                     ?? new TwilioLookupJson();

        return new PhoneLookupResult(
            lookup.Valid,
            lookup.PhoneNumber,
            lookup.ValidationErrors ?? Array.Empty<string>());
    }

    private static TwilioErrorJson? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorJson>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
