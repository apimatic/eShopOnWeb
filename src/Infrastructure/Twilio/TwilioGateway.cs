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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A small, hand-written client for the operations defined by the supplied
/// twilio_api_v2010 and twilio_lookups_v2 OpenAPI documents.
/// </summary>
public sealed class TwilioGateway : ITwilioGateway, IDisposable
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioGateway(IOptions<TwilioOptions> options)
        : this(options, new HttpClient(new HttpClientHandler(), disposeHandler: true))
    {
    }

    internal TwilioGateway(IOptions<TwilioOptions> options, HttpClient httpClient)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string input,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var path = "/v2/PhoneNumbers/" + Uri.EscapeDataString(input);
        using var request = CreateRequest(HttpMethod.Get, Combine(LookupsBaseUrl, path));
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null);
        }

        var payload = await ReadResponseAsync<LookupResponse>(response, "phone-number lookup", cancellationToken);
        return new PhoneNumberValidationResult(payload.Valid && !string.IsNullOrWhiteSpace(payload.PhoneNumber),
            payload.Valid ? payload.PhoneNumber : null);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesPath(), fields, "message creation", cancellationToken);
    }

    public Task<ProviderMessage> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        return SendAsync(HttpMethod.Get, MessagePath(providerMessageSid), null, "message fetch", cancellationToken);
    }

    public Task<ProviderMessage> CancelMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        return SendFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "message cancellation", cancellationToken);
    }

    public Task<ProviderMessage> RedactMessageContentAsync(string providerMessageSid,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        return SendFormAsync(HttpMethod.Post, MessagePath(providerMessageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message redaction", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        EnsureMessagingConfiguration();
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", _options.FromNumber),
            new("DateSent>", from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new("PageSize", "1000")
        };
        var nextPath = MessagesPath() + "?" + FormQuery(query);
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        var messages = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(nextPath) && seenPages.Add(nextPath))
        {
            using var request = CreateRequest(HttpMethod.Get, MessagingUri(nextPath));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var page = await ReadResponseAsync<MessageListResponse>(response, "message listing", cancellationToken);
            messages.AddRange(page.Messages.Select(ToProviderMessage));
            nextPath = NormalizeNextPage(page.NextPageUri);
        }

        return messages;
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
        using var request = CreateRequest(method, MessagingUri(path));
        request.Content = content;
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadResponseAsync<MessageResponse>(response, operation, cancellationToken);
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

    private async Task<T> ReadResponseAsync<T>(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResponse? error = null;
            try
            {
                error = await JsonSerializer.DeserializeAsync<TwilioErrorResponse>(stream, _jsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // Provider response text may contain PII, so it is intentionally not retained.
            }

            throw new TwilioRequestException(operation, (int)response.StatusCode, error?.Code);
        }

        var result = await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        return result ?? throw new TwilioRequestException(operation, (int)response.StatusCode, null);
    }

    private string MessagesPath() =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string sid) =>
        $"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    private Uri MessagingUri(string path) => Combine(
        string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl!, path);

    private static Uri Combine(string baseUrl, string path) =>
        new(baseUrl.TrimEnd('/') + "/" + path.TrimStart('/'), UriKind.Absolute);

    private static string FormQuery(IEnumerable<KeyValuePair<string, string>> values) =>
        string.Join("&", values.Select(value =>
            $"{Uri.EscapeDataString(value.Key)}={Uri.EscapeDataString(value.Value)}"));

    private string? NormalizeNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var pathAndQuery = Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute.PathAndQuery
            : nextPageUri;
        if (!ContainsQueryKey(pathAndQuery, "From"))
        {
            pathAndQuery += pathAndQuery.Contains('?') ? "&" : "?";
            pathAndQuery += FormQuery(new[] { new KeyValuePair<string, string>("From", _options.FromNumber) });
        }

        return pathAndQuery;
    }

    private static bool ContainsQueryKey(string uri, string key)
    {
        var queryStart = uri.IndexOf('?');
        if (queryStart < 0) return false;
        return uri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2)[0])
            .Any(name => string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase));
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }
    }

    private void EnsureMessagingConfiguration()
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio messaging settings are not configured.");
        }

        if (!string.IsNullOrWhiteSpace(_options.BaseUrl) &&
            !Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Twilio:BaseUrl must be an absolute URI.");
        }
    }

    private static ProviderMessage ToProviderMessage(MessageResponse message) => new(
        message.Sid ?? string.Empty,
        message.From,
        message.To,
        message.Status ?? "unknown",
        message.Body,
        ParseDate(message.DateCreated),
        ParseDate(message.DateSent),
        ParseDate(message.DateUpdated),
        message.ErrorCode);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    public void Dispose() => _httpClient.Dispose();

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("to")]
        public string? To { get; set; }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("body")]
        public string? Body { get; set; }
        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }
        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
        [JsonPropertyName("messages")]
        public List<MessageResponse> Messages { get; set; } = new();
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
