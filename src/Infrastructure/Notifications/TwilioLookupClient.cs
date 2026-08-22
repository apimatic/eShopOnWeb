using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public class TwilioLookupClient : TwilioHttpClientBase, ITwilioLookupClient
{
    private readonly HttpClient _httpClient;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
        : base(options, logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress ??= new Uri("https://lookups.twilio.com/");
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!phoneNumber.TrimStart().StartsWith('+') && !string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, path),
            retryServerErrors: true,
            cancellationToken);

        await EnsureSuccessAsync(response, "Lookup");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<LookupResponse>(stream, JsonOptions.Serializer, cancellationToken)
                      ?? new LookupResponse();

        var errors = payload.ValidationErrors ?? new List<string>();
        return new PhoneNumberLookupResult(
            payload.Valid,
            payload.PhoneNumber,
            payload.NationalFormat,
            payload.CountryCode,
            errors);
    }

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        public string? PhoneNumber { get; set; }
        public string? NationalFormat { get; set; }
        public string? CountryCode { get; set; }
        public List<string>? ValidationErrors { get; set; }
    }
}
