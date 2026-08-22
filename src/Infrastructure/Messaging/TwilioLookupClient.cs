using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioLookupClient : ITwilioLookupClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioOptions> _options;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var encoded = Uri.EscapeDataString(phoneNumber);
        var url = $"v2/PhoneNumbers/{encoded}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(options);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone lookup failed with HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var body = System.Text.Json.JsonSerializer.Deserialize<LookupResponseDto>(payload, TwilioHttp.JsonOptions);
        if (body is null)
        {
            return new PhoneLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        return new PhoneLookupResult(
            body.Valid,
            body.Valid ? body.PhoneNumber : null,
            body.ValidationErrors ?? Array.Empty<string>());
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string[]? ValidationErrors { get; set; }
    }
}
