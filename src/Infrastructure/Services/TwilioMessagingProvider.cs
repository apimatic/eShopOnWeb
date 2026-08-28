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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioMessagingProvider : IMessagingProvider, IDisposable
{
    private static readonly Uri DefaultMessagingBaseUri = new("https://api.twilio.com/");
    private static readonly Uri LookupBaseUri = new("https://lookups.twilio.com/");
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Uri _messagingBaseUri;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioMessagingProvider(IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        _messagingBaseUri = CreateBaseUri(_options.BaseUrl);
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = $"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, new Uri(LookupBaseUri, path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<LookupResponse>(response, "phone number validation", cancellationToken);
        return new PhoneNumberValidation(
            payload.Valid,
            payload.Valid ? payload.PhoneNumber : null,
            payload.ValidationErrors ?? Array.Empty<string>());
    }

    public async Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new MessagingProviderException("schedule message", null, "Twilio messaging service configuration is missing.");
            }

            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return await SendFormAsync(HttpMethod.Post, MessagesPath(), fields, "send message", cancellationToken);
    }

    public Task<ProviderMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        return SendAsync(HttpMethod.Get, MessagePath(providerMessageSid), null, "fetch message", cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        return SendFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, "cancel scheduled message", cancellationToken);
    }

    public Task<ProviderMessage> RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        return SendFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, "redact message", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSentAfter"] = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSentBefore"] = to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var path = MessagesPath() + "?" + string.Join("&", query.Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var messages = new List<ProviderMessage>();
        string? nextPath = path;

        while (!string.IsNullOrWhiteSpace(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(NormalizeNextPagePath(nextPath)));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var page = await ReadJsonAsync<MessageListResponse>(response, "list messages", cancellationToken);
            messages.AddRange(page.Messages.Select(ToProviderMessage));
            nextPath = page.NextPageUri;
        }

        return messages
            .Where(x => x.DateSent is { } sent && sent >= from && sent <= to)
            .ToList();
    }

    private async Task<ProviderMessage> SendFormAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>> fields, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        return await SendAsync(method, path, content, operation, cancellationToken);
    }

    private async Task<ProviderMessage> SendAsync(HttpMethod method, string path, HttpContent? content,
        string operation, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var request = CreateRequest(method, MessagingUri(path));
        request.Content = content;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync<MessageResponse>(response, operation, cancellationToken);
        return ToProviderMessage(payload);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            int? code = null;
            try
            {
                await using var errorStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var error = await JsonSerializer.DeserializeAsync<TwilioError>(errorStream, _jsonOptions, cancellationToken);
                code = error?.Code;
            }
            catch (JsonException)
            {
                // The response body is deliberately not included because it can contain a phone number.
            }

            throw new MessagingProviderException(operation, code, $"Twilio rejected the {operation} request.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        return payload ?? throw new MessagingProviderException(operation, null, $"Twilio returned an empty {operation} response.");
    }

    private ProviderMessage ToProviderMessage(MessageResponse message)
    {
        return new ProviderMessage(message.Sid, message.Status, message.Body, message.From, message.To,
            ParseDate(message.DateCreated), ParseDate(message.DateSent), message.ErrorCode, message.ErrorMessage);
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private string MessagesPath() => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";
    private Uri MessagingUri(string path) => new(_messagingBaseUri, path.TrimStart('/'));

    private static string NormalizeNextPagePath(string nextPageUri)
    {
        return Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery.TrimStart('/')
            : nextPageUri.TrimStart('/');
    }

    private static Uri CreateBaseUri(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return DefaultMessagingBaseUri;
        }

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute URI.");
        }

        return new Uri(uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/");
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new MessagingProviderException("authenticate", null, "Twilio configuration is incomplete.");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageListResponse
    {
        public MessageResponse[] Messages { get; set; } = Array.Empty<MessageResponse>();
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResponse
    {
        public string Sid { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }
    }

    private sealed class TwilioError
    {
        public int? Code { get; set; }
    }
}
