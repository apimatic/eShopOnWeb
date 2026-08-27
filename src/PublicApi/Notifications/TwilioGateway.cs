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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _messagingBaseUri;

    public TwilioGateway(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _messagingBaseUri = CreateBaseUri(string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.twilio.com"
            : _options.BaseUrl);
        _httpClient = new HttpClient(new SocketsHttpHandler())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<PhoneNumberLookup> ValidatePhoneNumberAsync(string suppliedNumber, CancellationToken cancellationToken)
    {
        EnsureAccountCredentials();
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(suppliedNumber)}";
        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupBaseUri, path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadPayloadAsync<LookupResponse>(response, cancellationToken);

        return new PhoneNumberLookup(
            payload.Valid,
            payload.Valid ? payload.PhoneNumber : null,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<TwilioMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return PostMessageAsync(MessagesPath(), values, cancellationToken);
    }

    public async Task<TwilioMessage> GetMessageAsync(string sid, CancellationToken cancellationToken)
    {
        EnsureAccountCredentials();
        using var request = CreateRequest(HttpMethod.Get, MessagingUri(MessagePath(sid)));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return Map(await ReadPayloadAsync<MessageResponse>(response, cancellationToken));
    }

    public Task<TwilioMessage> CancelScheduledMessageAsync(string sid, CancellationToken cancellationToken)
    {
        return PostMessageAsync(MessagePath(sid), new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);
    }

    public Task<TwilioMessage> RedactMessageContentAsync(string sid, CancellationToken cancellationToken)
    {
        return PostMessageAsync(MessagePath(sid), new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var uri = MessagingUri(MessagesPath() + "?" + EncodeQuery(query));
        var results = new List<TwilioMessage>();

        while (uri is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var page = await ReadPayloadAsync<MessageListResponse>(response, cancellationToken);
            results.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            uri = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : MessagingUri(page.NextPageUri);
        }

        return results
            .Where(x => x.DateSent >= from && x.DateSent <= to)
            .ToList();
    }

    private async Task<TwilioMessage> PostMessageAsync(
        string path,
        IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        using var request = CreateRequest(HttpMethod.Post, MessagingUri(path));
        request.Content = new FormUrlEncodedContent(values);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return Map(await ReadPayloadAsync<MessageResponse>(response, cancellationToken));
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, JsonOptions, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        return payload ?? throw new TwilioApiException((int)response.StatusCode, null);
    }

    private static TwilioMessage Map(MessageResponse response) => new(
        response.Sid ?? string.Empty,
        response.Status ?? "unknown",
        response.Body,
        response.From,
        response.To,
        response.ErrorCode,
        ParseDate(response.DateCreated),
        ParseDate(response.DateSent),
        ParseDate(response.SendAt));

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result)
            ? result
            : null;
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";
    private Uri MessagingUri(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
        {
            path = absolute.PathAndQuery;
        }

        return new Uri(_messagingBaseUri, path.TrimStart('/'));
    }

    private static Uri CreateBaseUri(string value)
    {
        if (!Uri.TryCreate(value.EndsWith('/') ? value : value + "/", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute URL.");
        }

        return uri;
    }

    private static string EncodeQuery(IReadOnlyDictionary<string, string> values) => string.Join(
        "&",
        values.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));

    private void EnsureAccountCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio account credentials are not configured.");
        }
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureAccountCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) || string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio messaging settings are not configured.");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        public bool Valid { get; set; }
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("send_at")]
        public string? SendAt { get; set; }
    }

    private sealed class MessageListResponse
    {
        public MessageResponse[]? Messages { get; set; }
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        public int? Code { get; set; }
    }
}
