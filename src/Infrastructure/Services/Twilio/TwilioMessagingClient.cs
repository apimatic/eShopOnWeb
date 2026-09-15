using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        if (_httpClient.BaseAddress == null)
        {
            var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
                ? DefaultBaseUrl
                : _settings.BaseUrl.TrimEnd('/');
            _httpClient.BaseAddress = new Uri(baseUrl + "/");
        }

        TwilioHttp.ApplyAuth(_httpClient, _settings);
    }

    public Task<TwilioMessageRecord> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageRecord> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageRecord?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessagePath(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ToRecord(DeserializeMessage(payload));
    }

    public async Task<TwilioMessageRecord> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        });
        using var response = await _httpClient.PostAsync(MessagePath(messageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ToRecord(DeserializeMessage(payload));
    }

    public async Task<TwilioMessageRecord> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        });
        using var response = await _httpClient.PostAsync(MessagePath(messageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ToRecord(DeserializeMessage(payload));
    }

    public async Task<IReadOnlyList<TwilioMessageRecord>> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromUtc = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var path =
            $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toUtc)}" +
            "&PageSize=1000";

        var results = new List<TwilioMessageRecord>();
        var nextPath = path;
        var pages = 0;

        while (!string.IsNullOrEmpty(nextPath) && pages < 100)
        {
            pages++;
            using var response = await _httpClient.GetAsync(nextPath, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, TwilioHttp.JsonOptions)
                       ?? new MessageListResponse();
            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToRecord(message));
                }
            }

            nextPath = ToRelativeMessagingPath(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageRecord> CreateMessageAsync(
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);
        return ToRecord(DeserializeMessage(payload));
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessagePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private string? ToRelativeMessagingPath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return nextPageUri.TrimStart('/');
    }

    private void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = TryReadError(payload);
        _logger.LogWarning(
            "Messaging API call failed with HTTP {StatusCode} code {ErrorCode}.",
            (int)response.StatusCode,
            error?.Code);
        throw new TwilioApiException((int)response.StatusCode, error?.Code);
    }

    private static TwilioErrorPayload? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorPayload>(payload, TwilioHttp.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MessageResource DeserializeMessage(string payload) =>
        JsonSerializer.Deserialize<MessageResource>(payload, TwilioHttp.JsonOptions)
        ?? throw new TwilioApiException(500, null);

    private static TwilioMessageRecord ToRecord(MessageResource message)
    {
        var errorCode = message.ErrorCode?.ToString(CultureInfo.InvariantCulture);
        return new TwilioMessageRecord(
            message.Sid ?? string.Empty,
            message.Status ?? "unknown",
            message.Body,
            message.To,
            message.From,
            ParseTwilioDate(message.DateSent),
            ParseTwilioDate(message.DateCreated),
            errorCode);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
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

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }
    }
}
