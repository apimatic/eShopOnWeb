using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["SmartEncoded"] = "true"
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["SmartEncoded"] = "true"
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        TwilioRequestException? lastNotFound = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return await UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
            }
            catch (TwilioRequestException ex) when (ex.StatusCode == 404 && attempt < 5)
            {
                lastNotFound = ex;
                var delayMs = 200 * (int)Math.Pow(2, attempt);
                _logger.LogWarning("Retrying cancel of a scheduled message after HTTP 404 (attempt {Attempt}).", attempt + 1);
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        throw lastNotFound!;
    }

    public Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendWithReadRetryAsync(() => CreateRequest(HttpMethod.Get, uri), cancellationToken);
        var payload = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToSnapshot(payload);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var startDate = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var endDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"From={Uri.EscapeDataString(fromNumber)}&DateSent%3E={startDate}&DateSent%3C={endDate}&PageSize=1000";
        var nextUri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}");

        var results = new List<TwilioMessageSnapshot>();
        while (nextUri != null)
        {
            var pageUri = nextUri;
            using var response = await SendWithReadRetryAsync(() => CreateRequest(HttpMethod.Get, pageUri), cancellationToken);
            var page = await ReadJsonAsync<TwilioMessageListResource>(response, cancellationToken);
            if (page.Messages != null)
            {
                results.AddRange(page.Messages
                    .Where(message => string.Equals(message.Direction, "outbound-api", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(message.Direction, "outbound-reply", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(message.Direction, "outbound-call", StringComparison.OrdinalIgnoreCase))
                    .Select(ToSnapshot));
            }

            nextUri = string.IsNullOrEmpty(page.NextPageUri) ? null : ResolveMessagingUri(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> CreateMessageAsync(IDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json");
        using var request = CreateRequest(HttpMethod.Post, uri);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToSnapshot(payload);
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(string messageSid, IDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json");
        using var request = CreateRequest(HttpMethod.Post, uri);
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<TwilioMessageResource>(response, cancellationToken);
        return ToSnapshot(payload);
    }

    private async Task<HttpResponseMessage> SendWithReadRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            response?.Dispose();
            using var request = requestFactory();
            response = await _httpClient.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode != 429 && (int)response.StatusCode != 500 && (int)response.StatusCode != 503)
            {
                return response;
            }

            if (attempt == maxAttempts - 1)
            {
                return response;
            }

            var delayMs = (int)Math.Min(30_000, 500 * Math.Pow(2, attempt));
            if (response.Headers.RetryAfter?.Delta is TimeSpan retryAfter)
            {
                delayMs = (int)Math.Max(delayMs, retryAfter.TotalMilliseconds);
            }

            _logger.LogWarning("Retrying a Twilio messaging read after HTTP {Status} (attempt {Attempt}).", (int)response.StatusCode, attempt + 1);
            await Task.Delay(delayMs, cancellationToken);
        }

        return response!;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return request;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<TwilioErrorResource>(json);
            var code = error?.Code;
            _logger.LogWarning("Twilio messaging call failed with HTTP {Status} and error code {ErrorCode}.", (int)response.StatusCode, code);
            throw new TwilioRequestException((int)response.StatusCode, code, "Twilio messaging request failed.");
        }

        var payload = JsonSerializer.Deserialize<T>(json, JsonOptions);
        if (payload is null)
        {
            throw new TwilioRequestException((int)response.StatusCode, null, "Twilio messaging returned an empty body.");
        }

        return payload;
    }

    private static T? TryDeserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private Uri MessagingUri(string relativePathAndQuery)
    {
        var root = GetMessagingRoot();
        return new Uri($"{root}/{relativePathAndQuery.TrimStart('/')}", UriKind.Absolute);
    }

    private Uri ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri($"{GetMessagingRoot()}{absolute.PathAndQuery}", UriKind.Absolute);
        }

        return MessagingUri(nextPageUri);
    }

    private string GetMessagingRoot()
    {
        return string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new TwilioMessageSnapshot
        {
            Sid = resource.Sid ?? string.Empty,
            Status = resource.Status ?? string.Empty,
            ErrorCode = resource.ErrorCode,
            Body = resource.Body,
            To = resource.To,
            From = resource.From,
            Direction = resource.Direction,
            DateSent = ParseRfc2822(resource.DateSent),
            DateCreated = ParseRfc2822(resource.DateCreated)
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("direction")]
        public string? Direction { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioMessageListResource
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResource
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
