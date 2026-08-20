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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string FromNumber => _options.FromNumber;

    public async Task<TwilioMessageSnapshot> SendAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["From"] = _options.FromNumber
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages with the provider.");
            }

            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(fields);
        using var request = CreateRequest(HttpMethod.Post, MessagesCollectionPath(), content);
        return await SendAndReadMessageAsync(request, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, MessageInstancePath(messageSid));
        return await SendAndReadMessageAsync(request, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var pageUrl = BuildListUrl(fromNumber, from, to);

        while (!string.IsNullOrEmpty(pageUrl))
        {
            using var request = CreateRequestFromUrl(HttpMethod.Get, pageUrl);
            using var response = await SendAuthenticatedAsync(request, cancellationToken);
            var payload = await ReadJsonAsync<TwilioMessageListPayload>(response, cancellationToken);
            if (payload?.Messages is not null)
            {
                results.AddRange(payload.Messages.Select(ToSnapshot));
            }

            pageUrl = string.IsNullOrEmpty(payload?.NextPageUri)
                ? null
                : CombineWithMessagingBase(payload.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(string messageSid, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var request = CreateRequest(HttpMethod.Post, MessageInstancePath(messageSid), content);
        return await SendAndReadMessageAsync(request, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> SendAndReadMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await SendAuthenticatedAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<TwilioMessagePayload>(response, cancellationToken);
        if (payload is null || string.IsNullOrEmpty(payload.Sid))
        {
            throw new TwilioApiException((int)response.StatusCode, null, "The provider returned an empty message resource.");
        }

        return ToSnapshot(payload);
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BuildBasicAuthToken());
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        var error = await TryReadErrorAsync(response, cancellationToken);
        _logger.LogWarning("Twilio messaging API returned {StatusCode} with provider code {ErrorCode}", (int)response.StatusCode, error?.Code);
        throw new TwilioApiException((int)response.StatusCode, error?.Code, "The messaging provider rejected the request.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        return CreateRequestFromUrl(method, CombineWithMessagingBase(path), content);
    }

    private static HttpRequestMessage CreateRequestFromUrl(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (content is not null)
        {
            request.Content = content;
        }

        return request;
    }

    private string MessagesCollectionPath()
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstancePath(string messageSid)
        => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private string BuildListUrl(string fromNumber, DateTimeOffset from, DateTimeOffset to)
    {
        var path = MessagesCollectionPath();
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var query =
            $"From={Uri.EscapeDataString(fromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
            "&PageSize=1000";
        return CombineWithMessagingBase($"{path}?{query}");
    }

    private string CombineWithMessagingBase(string pathOrUrl)
    {
        var root = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.twilio.com"
            : _options.BaseUrl.TrimEnd('/');

        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var absolute))
        {
            return $"{root}{absolute.PathAndQuery}";
        }

        if (!pathOrUrl.StartsWith('/'))
        {
            pathOrUrl = "/" + pathOrUrl;
        }

        return root + pathOrUrl;
    }

    private string BuildBasicAuthToken()
    {
        return Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task<TwilioErrorPayload?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadJsonAsync<TwilioErrorPayload>(response, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessagePayload payload)
    {
        return new TwilioMessageSnapshot
        {
            Sid = payload.Sid ?? string.Empty,
            Status = payload.Status ?? string.Empty,
            Body = payload.Body,
            DateSent = ParseTwilioDate(payload.DateSent),
            DateCreated = ParseTwilioDate(payload.DateCreated),
            ErrorCode = payload.ErrorCode,
            ErrorMessage = payload.ErrorMessage,
            From = payload.From
        };
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

    private sealed class TwilioMessageListPayload
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessagePayload>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessagePayload
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }
    }

    private sealed class TwilioErrorPayload
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
