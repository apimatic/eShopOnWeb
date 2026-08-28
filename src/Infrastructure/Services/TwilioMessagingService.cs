using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioMessagingService : ITwilioMessagingService, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com/";
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _messagingBaseUri;

    public TwilioMessagingService(IOptions<TwilioOptions> options)
        : this(options, new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10)
        })
    {
    }

    internal TwilioMessagingService(IOptions<TwilioOptions> options, HttpMessageHandler handler)
    {
        _options = options.Value;
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl;
        _messagingBaseUri = new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        // This client is intentionally not created through IHttpClientFactory. Its default
        // logging handlers include request URLs, and Lookup puts the shopper's number in the path.
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<string?> ValidateAndNormalizeAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureAccountConfigured();
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return null;
        }

        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        using var response = await SendRequestAsync(HttpMethod.Get, new Uri(LookupBaseUri, path), null, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return null;
        }

        var result = await ReadAsync<LookupResponse>(response, cancellationToken);
        EnsureSuccess(response, result.ErrorCode);
        return result.Valid && !string.IsNullOrWhiteSpace(result.PhoneNumber) ? result.PhoneNumber : null;
    }

    public Task<TwilioMessageState> SendAsync(string to, string body, CancellationToken cancellationToken)
    {
        return CreateMessageAsync(to, body, null, cancellationToken);
    }

    public Task<TwilioMessageState> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        EnsureMessagingServiceConfigured();
        return CreateMessageAsync(to, body, sendAt, cancellationToken);
    }

    public async Task<TwilioMessageState> FetchAsync(string messageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        using var response = await SendRequestAsync(HttpMethod.Get, MessageUri(messageSid), null, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<TwilioMessageState> CancelAsync(string messageSid, CancellationToken cancellationToken)
    {
        return UpdateMessageAsync(messageSid, new[] { Pair("Status", "canceled") }, cancellationToken);
    }

    public Task<TwilioMessageState> RedactAsync(string messageSid, CancellationToken cancellationToken)
    {
        return UpdateMessageAsync(messageSid, new[] { Pair("Body", string.Empty) }, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageState>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        var parameters = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var firstPath = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json?{ToQueryString(parameters)}";
        Uri? next = MessagingUri(firstPath);
        var messages = new List<TwilioMessageState>();

        while (next is not null)
        {
            using var response = await SendRequestAsync(HttpMethod.Get, next, null, cancellationToken);
            var page = await ReadAsync<MessagePageResponse>(response, cancellationToken);
            EnsureSuccess(response, page.ErrorCode);
            messages.AddRange(page.Messages.Select(ToState));
            next = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : MessagingUriFromPage(page.NextPageUri);
        }

        return messages
            .Where(x => x.DateSent >= from && x.DateSent <= to)
            .ToList();
    }

    private async Task<TwilioMessageState> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        var values = new List<KeyValuePair<string, string>>
        {
            Pair("To", to),
            Pair("From", _options.FromNumber),
            Pair("Body", body)
        };
        if (sendAt.HasValue)
        {
            values.Add(Pair("MessagingServiceSid", _options.MessagingServiceSid));
            values.Add(Pair("ScheduleType", "fixed"));
            values.Add(Pair("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        var path = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
        using var content = new FormUrlEncodedContent(values);
        using var response = await SendRequestAsync(HttpMethod.Post, MessagingUri(path), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageState> UpdateMessageAsync(string messageSid, IEnumerable<KeyValuePair<string, string>> values, CancellationToken cancellationToken)
    {
        EnsureMessagingConfigured();
        using var content = new FormUrlEncodedContent(values);
        using var response = await SendRequestAsync(HttpMethod.Post, MessageUri(messageSid), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageState> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var result = await ReadAsync<MessageResponse>(response, cancellationToken);
        EnsureSuccess(response, result.ErrorCode);
        if (string.IsNullOrWhiteSpace(result.Sid) || string.IsNullOrWhiteSpace(result.Status))
        {
            throw new TwilioApiException(response.StatusCode, result.ErrorCode);
        }

        return ToState(result);
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, Uri uri, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : ProviderResponse, new()
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken) ?? new T();
        }
        catch (JsonException)
        {
            throw new TwilioApiException(response.StatusCode, null);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, int? errorCode)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(response.StatusCode, errorCode);
        }
    }

    private Uri MessageUri(string messageSid)
    {
        var path = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        return MessagingUri(path);
    }

    private Uri MessagingUri(string relativePath) => new(_messagingBaseUri, relativePath);

    private Uri MessagingUriFromPage(string pageUri)
    {
        var parsed = new Uri(pageUri, UriKind.RelativeOrAbsolute);
        var relativePath = parsed.IsAbsoluteUri ? parsed.PathAndQuery : pageUri;
        return MessagingUri(relativePath.TrimStart('/'));
    }

    private void EnsureAccountConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio account credentials are not configured.");
        }
    }

    private void EnsureMessagingConfigured()
    {
        EnsureAccountConfigured();
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }
    }

    private void EnsureMessagingServiceConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is not configured.");
        }
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private static string ToQueryString(IReadOnlyDictionary<string, string> values) => string.Join("&", values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private static TwilioMessageState ToState(MessageResponse message) => new(
        message.Sid ?? string.Empty,
        message.Status ?? "unknown",
        message.MessageErrorCode,
        ParseDate(message.DateCreated),
        ParseDate(message.DateSent),
        message.Body);

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    public void Dispose() => _httpClient.Dispose();

    private class ProviderResponse
    {
        [JsonPropertyName("code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class LookupResponse : ProviderResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
    }

    private sealed class MessageResponse : ProviderResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("error_code")]
        public int? MessageErrorCode { get; set; }
    }

    private sealed class MessagePageResponse : ProviderResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
