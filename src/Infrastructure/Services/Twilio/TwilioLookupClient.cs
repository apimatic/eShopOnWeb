using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioLookupClient : ITwilioLookupClient
{
    private static readonly Uri LookupBaseAddress = new("https://lookups.twilio.com/");

    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioSettings> _options;
    private readonly ILogger<TwilioLookupClient> _logger;

    public TwilioLookupClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioLookupClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = LookupBaseAddress;
        }
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        TwilioRequestHelper.EnsureCredentials(settings);

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = TwilioRequestHelper.CreateBasicAuth(settings);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TwilioRequestHelper.TryReadError(payload);
            _logger.LogWarning("Twilio Lookup failed with HTTP {Status} and provider code {Code}.", (int)response.StatusCode, error?.Code);
            throw new InvalidOperationException("The phone number could not be validated with the messaging provider.");
        }

        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, TwilioRequestHelper.JsonOptions);
        if (lookup is null)
        {
            throw new InvalidOperationException("The phone number could not be validated with the messaging provider.");
        }

        return new PhoneNumberLookupResult
        {
            Valid = lookup.Valid,
            PhoneNumber = lookup.PhoneNumber,
            NationalFormat = lookup.NationalFormat,
            CountryCode = lookup.CountryCode,
            ValidationErrors = (IReadOnlyList<string>?)lookup.ValidationErrors ?? Array.Empty<string>()
        };
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

internal static class TwilioRequestHelper
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static void EnsureCredentials(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }
    }

    public static AuthenticationHeaderValue CreateBasicAuth(TwilioSettings settings)
    {
        var raw = $"{settings.AccountSid}:{settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static TwilioErrorBody? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorBody>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Uri ResolveMessagingBaseAddress(string? configuredBaseUrl)
    {
        var value = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? "https://api.twilio.com"
            : configuredBaseUrl.Trim();
        return new Uri(value.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public static Uri Combine(Uri baseAddress, string pathAndQuery)
    {
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out var absolute))
        {
            pathAndQuery = absolute.PathAndQuery;
        }

        var basePath = baseAddress.AbsolutePath.TrimEnd('/');
        if (pathAndQuery.StartsWith('/'))
        {
            return new Uri(baseAddress, $"{basePath}{pathAndQuery}");
        }

        return new Uri(baseAddress, pathAndQuery);
    }
}

internal sealed class TwilioErrorBody
{
    public int? Code { get; set; }
    public string? Message { get; set; }
    public int? Status { get; set; }
}

internal sealed class TwilioMessageResource
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public int? ErrorCode { get; set; }
    public string? Body { get; set; }
    public string? From { get; set; }
    [JsonPropertyName("date_sent")]
    public string? DateSent { get; set; }
    [JsonPropertyName("date_created")]
    public string? DateCreated { get; set; }
}

internal sealed class TwilioMessageListResponse
{
    public List<TwilioMessageResource>? Messages { get; set; }
    [JsonPropertyName("next_page_uri")]
    public string? NextPageUri { get; set; }
}
