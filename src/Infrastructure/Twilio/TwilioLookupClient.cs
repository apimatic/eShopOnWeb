using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = TwilioAuth.RequireConfigured(options);
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioAuth.CreateBasicHeader(_options);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(
                (int)response.StatusCode,
                $"Twilio Lookup rejected the request ({(int)response.StatusCode}). {PhoneNumberRedactor.Redact(ExtractErrorMessage(payload))}");
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions)
            ?? throw new TwilioApiException((int)response.StatusCode, "Twilio Lookup returned an empty response.");

        var errors = (IReadOnlyList<string>)(lookup.ValidationErrors ?? new List<string>());
        return new PhoneLookupResult(lookup.Valid, lookup.PhoneNumber, errors);
    }

    private static string ExtractErrorMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "Twilio Lookup error";
            }
        }
        catch (JsonException)
        {
            // Fall through — body is not JSON.
        }

        return "Twilio Lookup error";
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }
}
