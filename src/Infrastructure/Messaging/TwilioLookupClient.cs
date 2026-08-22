using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : IPhoneNumberLookup
{
    public const string HttpClientName = "TwilioLookup";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioOptions> _options;
    private readonly IAppLogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        IAppLogger<TwilioLookupClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        // Lookup v2 basic request: GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        var path = "v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = CreateBasicAuth(options);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup request failed with HTTP {Status}", (int)response.StatusCode);
            throw new InvalidOperationException($"Phone number lookup failed with HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(string.IsNullOrEmpty(payload) ? "{}" : payload);
        var root = document.RootElement;
        var valid = root.TryGetProperty("valid", out var validElement) && validElement.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var phoneElement) && phoneElement.ValueKind == JsonValueKind.String
            ? phoneElement.GetString()
            : null;

        return new PhoneNumberLookupResult(valid, valid ? canonical : null);
    }

    private static AuthenticationHeaderValue CreateBasicAuth(TwilioOptions options)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }
}
