using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio Lookups v2 client. Contract: api-specs/twilio/twilio_lookups_v2/twilio_lookups_v2.yaml
/// GET /v2/PhoneNumbers/{PhoneNumber} on https://lookups.twilio.com (not governed by Twilio:BaseUrl).
/// </summary>
public class TwilioLookupsClient : ITwilioLookupsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioLookupsClient> _logger;

    public TwilioLookupsClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupsClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        TwilioAuth.EnsureConfigured(_settings);

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_settings);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider lookup request failed.");
            throw;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_FOUND" });
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Provider lookup returned HTTP {StatusCode}.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var payload = await response.Content.ReadFromJsonAsync<LookupResponseDto>(JsonOptions, cancellationToken);
        if (payload is null)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "LOOKUP_FAILED" });
        }

        var errors = (IReadOnlyList<string>)(payload.ValidationErrors ?? new List<string>());
        var isValid = payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber);
        return new PhoneNumberLookupResult(isValid, payload.PhoneNumber, errors);
    }

    private sealed class LookupResponseDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
