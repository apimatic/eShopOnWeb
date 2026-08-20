using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio Lookup v2 basic (free) request for E.164 formatting and the <c>valid</c> flag.
/// Confirmed against https://www.twilio.com/docs/lookup/v2-api and
/// https://www.twilio.com/docs/lookup/v2-api/formatting-validation
/// GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
/// This host is not governed by Twilio:BaseUrl.
/// </summary>
public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _httpClient.BaseAddress ??= new Uri("https://lookups.twilio.com");
    }

    public async Task<TwilioLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v2/PhoneNumbers/{encoded}");
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var sanitized = PhoneNumberSanitizer.Redact(payload);
            throw new HttpRequestException($"Twilio Lookup request failed ({(int)response.StatusCode}): {sanitized}");
        }

        var dto = JsonSerializer.Deserialize<LookupDto>(payload, JsonOptions) ?? new LookupDto();
        var error = dto.ValidationErrors is { Count: > 0 }
            ? string.Join(",", dto.ValidationErrors)
            : null;

        return new TwilioLookupResult(dto.Valid, dto.PhoneNumber, error);
    }

    private sealed class LookupDto
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
