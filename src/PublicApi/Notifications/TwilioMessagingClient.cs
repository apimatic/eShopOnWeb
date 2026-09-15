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

public sealed class TwilioMessagingClient : ITwilioMessagingClient, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _messagingClient = new(new SocketsHttpHandler());
    private readonly HttpClient _lookupClient = new(new SocketsHttpHandler());
    private readonly AuthenticationHeaderValue _authorization;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioMessagingClient(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        ValidateOptions(_options);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authorization = new AuthenticationHeaderValue("Basic", credentials);
        _messagingClient.Timeout = TimeSpan.FromSeconds(30);
        _lookupClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<PhoneNumberLookup> LookupPhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var path = $"/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            path += $"?CountryCode={Uri.EscapeDataString(countryCode.ToUpperInvariant())}";
        }

        using var request = CreateRequest(HttpMethod.Get, CombineUrl(LookupBaseUrl, path));
        using var response = await _lookupClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadPayloadAsync<LookupResponse>(response, cancellationToken);
        return new PhoneNumberLookup(payload.Valid, payload.PhoneNumber, payload.ValidationErrors ?? Array.Empty<string>());
    }

    public Task<TwilioMessage> SendMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendMessageRequestAsync(HttpMethod.Post, MessagesPath(), form, cancellationToken);
    }

    public Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(HttpMethod.Get, MessagePath(messageSid), null, cancellationToken);

    public Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            cancellationToken);

    public Task<TwilioMessage> RedactMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        SendMessageRequestAsync(
            HttpMethod.Post,
            MessagePath(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            cancellationToken);

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = new[]
        {
            Pair("From", _options.FromNumber),
            Pair("DateSent>", from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            Pair("PageSize", "1000")
        };
        var nextPath = $"{MessagesPath()}?{string.Join("&", query)}";
        var messages = new List<TwilioMessage>();

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUrl(nextPath));
            using var response = await _messagingClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var page = await ReadPayloadAsync<MessageListResponse>(response, cancellationToken);
            messages.AddRange((page.Messages ?? Array.Empty<MessageResponse>()).Select(Map));
            nextPath = NormalizeProviderPageUri(page.NextPageUri);
        }

        return messages
            .Where(message => (message.DateSent ?? message.DateCreated) is { } timestamp && timestamp >= from && timestamp <= to)
            .ToList();
    }

    private async Task<TwilioMessage> SendMessageRequestAsync(
        HttpMethod method,
        string path,
        IEnumerable<KeyValuePair<string, string>>? form,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, MessagingUrl(path));
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        using var response = await _messagingClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var payload = await ReadPayloadAsync<MessageResponse>(response, cancellationToken);
        return Map(payload);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = _authorization;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<T> ReadPayloadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await JsonSerializer.DeserializeAsync<ErrorResponse>(stream, _jsonOptions, cancellationToken);
            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        var payload = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        return payload ?? throw new TwilioApiException((int)response.StatusCode, null);
    }

    private string MessagesPath() => $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private string MessagingUrl(string path) =>
        CombineUrl(string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl, path);

    private static string CombineUrl(string baseUrl, string path) => $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    private static string Pair(string name, string value) =>
        $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";

    private static string? NormalizeProviderPageUri(string? pageUri)
    {
        if (string.IsNullOrWhiteSpace(pageUri))
        {
            return null;
        }

        return Uri.TryCreate(pageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : pageUri;
    }

    private static TwilioMessage Map(MessageResponse value) => new(
        value.Sid ?? string.Empty,
        value.Status ?? string.Empty,
        value.Body,
        value.ErrorCode,
        ParseProviderDate(value.DateCreated),
        ParseProviderDate(value.DateSent));

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static void ValidateOptions(TwilioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) ||
            string.IsNullOrWhiteSpace(options.AuthToken) ||
            string.IsNullOrWhiteSpace(options.FromNumber) ||
            string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio configuration is incomplete.");
        }
    }

    public void Dispose()
    {
        _messagingClient.Dispose();
        _lookupClient.Dispose();
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public MessageResponse[]? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
