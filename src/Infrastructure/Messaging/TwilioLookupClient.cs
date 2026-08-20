using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encodedNumber = Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/PhoneNumbers/{encodedNumber}");
        request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("The messaging provider could not look up the supplied number.");
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new PhoneNumberLookupResult(false, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException("The messaging provider rejected the number lookup.");
            }

            TwilioLookupResponse? payload;
            try
            {
                payload = await response.Content.ReadFromJsonAsync<TwilioLookupResponse>(JsonOptions, cancellationToken);
            }
            catch (Exception)
            {
                throw new InvalidOperationException("The messaging provider returned an unreadable lookup response.");
            }

            if (payload is null || !payload.Valid || string.IsNullOrWhiteSpace(payload.PhoneNumber))
            {
                return new PhoneNumberLookupResult(false, payload?.PhoneNumber);
            }

            return new PhoneNumberLookupResult(true, payload.PhoneNumber);
        }
    }
}
