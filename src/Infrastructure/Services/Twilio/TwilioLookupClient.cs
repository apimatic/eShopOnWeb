using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}");
        ApplyBasicAuth(request);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderException("The messaging provider could not look up the destination.");
        }

        var parsed = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (parsed == null)
        {
            throw new ProviderException("The messaging provider returned an unreadable lookup response.");
        }

        return new PhoneNumberLookupResult(parsed.Valid, parsed.Valid ? parsed.PhoneNumber : null);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var encoded = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
    }
}
