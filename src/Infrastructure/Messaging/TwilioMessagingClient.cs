using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioOptions> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string FromNumber => _options.FromNumber;

    public Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("Body", body)
        };
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("Body", body),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };
        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(MessageResourcePath(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new[] { new KeyValuePair<string, string>("Status", "canceled") };
        return UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new[] { new KeyValuePair<string, string>("Body", string.Empty) };
        return UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var path = MessagesCollectionPath();
        var query = new Dictionary<string, string>
        {
            ["From"] = fromNumber,
            ["DateSent>="] = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["DateSent<="] = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        var uri = AppendQuery(path, query);

        while (!string.IsNullOrWhiteSpace(uri))
        {
            var requestUri = ResolveMessagingUri(uri);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateProviderException(payload, (int)response.StatusCode);
            }

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions)
                ?? new MessageListResponse();
            if (page.Messages != null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(ToSnapshot(item));
                }
            }

            uri = page.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> CreateMessageAsync(
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(
        string messageSid,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        var response = await _httpClient.PostAsync(MessageResourcePath(messageSid), content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException(payload, (int)response.StatusCode);
        }

        var document = JsonSerializer.Deserialize<MessageResource>(payload, JsonOptions);
        if (document == null || string.IsNullOrWhiteSpace(document.Sid))
        {
            throw new InvalidOperationException("Twilio returned an empty message resource.");
        }

        return ToSnapshot(document);
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageResourcePath(string messageSid) =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";

    private Uri ResolveMessagingUri(string uri)
    {
        if (uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = new Uri(uri, UriKind.Absolute);
            return new Uri(_httpClient.BaseAddress ?? new Uri("https://api.twilio.com/"), parsed.PathAndQuery);
        }

        return new Uri(_httpClient.BaseAddress ?? new Uri("https://api.twilio.com/"), uri);
    }

    private static string AppendQuery(string path, IReadOnlyDictionary<string, string> query)
    {
        var parts = new List<string>();
        foreach (var pair in query)
        {
            parts.Add($"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");
        }

        return $"{path}?{string.Join("&", parts)}";
    }

    private Exception CreateProviderException(string payload, int statusCode)
    {
        var sanitized = LogSanitizer.RedactPhoneNumbers(payload);
        _logger.LogWarning("Twilio Messaging returned {StatusCode}: {Message}", statusCode, sanitized);

        try
        {
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, JsonOptions);
            if (!string.IsNullOrWhiteSpace(error?.Message))
            {
                return new TwilioProviderException(LogSanitizer.RedactPhoneNumbers(error.Message), statusCode);
            }
        }
        catch (JsonException)
        {
            // Fall through to the generic error.
        }

        return new TwilioProviderException($"Twilio Messaging request failed with HTTP {statusCode}.", statusCode);
    }

    private static TwilioMessageSnapshot ToSnapshot(MessageResource resource)
    {
        return new TwilioMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status,
            resource.Body,
            resource.From,
            resource.To,
            resource.ErrorCode,
            resource.ErrorMessage,
            ParseTwilioDate(resource.DateCreated),
            ParseTwilioDate(resource.DateSent));
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
