using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

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
        _options = options.Value;
        ApplyAuthentication(_httpClient, _options);
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PhoneNumberLookupResult
            {
                IsValid = false,
                ValidationErrors = new[] { $"lookup_http_{(int)response.StatusCode}" }
            };
        }

        var document = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);
        if (document is null)
        {
            return new PhoneNumberLookupResult { IsValid = false, ValidationErrors = new[] { "unreadable_lookup_response" } };
        }

        return new PhoneNumberLookupResult
        {
            IsValid = document.Valid,
            CanonicalPhoneNumber = document.PhoneNumber,
            ValidationErrors = (IReadOnlyList<string>)(document.ValidationErrors ?? new List<string>())
        };
    }

    internal static void ApplyAuthentication(HttpClient httpClient, TwilioOptions options)
    {
        if (httpClient.DefaultRequestHeaders.Authorization is not null)
        {
            return;
        }

        var token = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
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
