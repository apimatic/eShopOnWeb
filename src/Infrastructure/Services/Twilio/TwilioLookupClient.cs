using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Lookups v2 FetchPhoneNumber — GET /v2/PhoneNumbers/{PhoneNumber} on lookups.twilio.com.
/// This host is not governed by Twilio:BaseUrl.
/// </summary>
public class TwilioLookupClient : IPhoneNumberLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress ??= new Uri("https://lookups.twilio.com/");
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Phone number lookup was rejected by the provider with status {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, Array.Empty<string>());
        }

        var payload = await response.Content.ReadFromJsonAsync<LookupResponseDto>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return new PhoneNumberLookupResult(false, null, Array.Empty<string>());
        }

        var errors = (IReadOnlyList<string>)(payload.ValidationErrors ?? new List<string>());
        return new PhoneNumberLookupResult(payload.Valid, payload.PhoneNumber, errors);
    }
}
