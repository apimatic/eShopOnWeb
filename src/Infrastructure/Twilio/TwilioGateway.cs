using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioGateway : ITwilioGateway
{
    public const string HttpClientName = "Twilio";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com/2010-04-01";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioGateway> _logger;
    private readonly string _basicAuth;

    public TwilioGateway(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> options, ILogger<TwilioGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
        _basicAuth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var encoded = Uri.EscapeDataString(phoneNumber);
        var uri = new Uri($"{LookupBaseUrl}/v2/PhoneNumbers/{encoded}");
        using var response = await SendAsync(HttpMethod.Get, uri, content: null, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = await TryReadErrorCodeAsync(response, cancellationToken);
            _logger.LogWarning("Twilio Lookup failed with HTTP {StatusCode} provider {ProviderCode}.", (int)response.StatusCode, errorCode);
            if ((int)response.StatusCode == 404)
            {
                return new TwilioLookupResult(false, null, new[] { "NOT_FOUND" });
            }

            throw new TwilioGatewayException("lookup", (int)response.StatusCode, errorCode);
        }

        var payload = await ReadJsonAsync<TwilioLookupResponse>(response, cancellationToken);
        var errors = payload.ValidationErrors ?? new List<string>();
        return new TwilioLookupResult(payload.Valid, payload.Valid ? payload.PhoneNumber : null, errors);
    }

    public async Task<TwilioMessageSnapshot> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields.Add(new("From", _settings.FromNumber));
        }

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Scheduled messages require Twilio:MessagingServiceSid.");
            }

            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        var uri = MessagingUri($"Accounts/{_settings.AccountSid}/Messages.json");
        using var response = await SendAsync(HttpMethod.Post, uri, content, cancellationToken);
        return await ReadMessageSnapshotAsync(response, "send", cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var uri = MessagingUri($"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendAsync(HttpMethod.Get, uri, content: null, cancellationToken);
        return await ReadMessageSnapshotAsync(response, "fetch", cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        var uri = MessagingUri($"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendAsync(HttpMethod.Post, uri, content, cancellationToken);
        return await ReadMessageSnapshotAsync(response, "cancel", cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        var uri = MessagingUri($"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendAsync(HttpMethod.Post, uri, content, cancellationToken);
        return await ReadMessageSnapshotAsync(response, "redact", cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var fromFormatted = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toFormatted = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var query = string.Join("&",
            "From=" + Uri.EscapeDataString(fromNumber),
            Uri.EscapeDataString("DateSent>") + "=" + Uri.EscapeDataString(fromFormatted),
            Uri.EscapeDataString("DateSent<") + "=" + Uri.EscapeDataString(toFormatted),
            "PageSize=1000");

        var nextUrl = MessagingUri($"Accounts/{_settings.AccountSid}/Messages.json?{query}");

        while (nextUrl != null)
        {
            using var response = await SendAsync(HttpMethod.Get, nextUrl, content: null, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorCode = await TryReadErrorCodeAsync(response, cancellationToken);
                _logger.LogWarning("Twilio list messages failed with HTTP {StatusCode} provider {ProviderCode}.", (int)response.StatusCode, errorCode);
                throw new TwilioGatewayException("list", (int)response.StatusCode, errorCode);
            }

            var payload = await ReadJsonAsync<TwilioMessageListResponse>(response, cancellationToken);
            if (payload.Messages != null)
            {
                foreach (var message in payload.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            nextUrl = ResolveNextPage(payload.NextPageUri);
        }

        return results;
    }

    private Uri? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return MessagingUri($"Accounts/{_settings.AccountSid}/Messages.json");
        }

        return MessagingUri($"Accounts/{_settings.AccountSid}/Messages.json{nextPageUri[queryIndex..]}");
    }

    private Uri MessagingUri(string relativePathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
        return new Uri($"{baseUrl}/{relativePathAndQuery.TrimStart('/')}");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuth);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> ReadMessageSnapshotAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var errorCode = await TryReadErrorCodeAsync(response, cancellationToken);
            _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode} provider {ProviderCode}.", operation, (int)response.StatusCode, errorCode);
            throw new TwilioGatewayException(operation, (int)response.StatusCode, errorCode);
        }

        var payload = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToSnapshot(payload);
    }

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return result ?? throw new TwilioGatewayException("deserialize", (int)response.StatusCode, null);
    }

    private static async Task<int?> TryReadErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var error = await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, JsonOptions, cancellationToken);
            return error?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new TwilioMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status ?? string.Empty,
            resource.Body,
            resource.ErrorCode,
            ParseTwilioDate(resource.DateSent),
            ParseTwilioDate(resource.DateCreated),
            resource.From);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioLookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
