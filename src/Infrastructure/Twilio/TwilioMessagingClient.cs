using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<TwilioMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageResult?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, MessageInstancePath(messageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio fetch message {MessageSid} failed with HTTP {StatusCode}.", messageSid, (int)response.StatusCode);
            throw new TwilioApiException(TwilioHttp.FormatError((int)response.StatusCode, payload));
        }

        return ParseMessage(payload);
    }

    public async Task<TwilioMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        await UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageResult>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromFormatted = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toFormatted = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var path =
            $"{MessagesListPath()}?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromFormatted)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toFormatted)}" +
            "&PageSize=1000";

        var results = new List<TwilioMessageResult>();
        string? next = path;

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, next);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio list messages failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new TwilioApiException(TwilioHttp.FormatError((int)response.StatusCode, payload));
            }

            var page = JsonSerializer.Deserialize<MessageListDto>(payload, TwilioHttp.JsonOptions)
                ?? new MessageListDto();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToResult(message));
                }
            }

            next = ResolveNextPage(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageResult> CreateMessageAsync(
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, MessagesListPath());
        request.Content = new FormUrlEncodedContent(fields);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio create message failed with HTTP {StatusCode}.", (int)response.StatusCode);
            throw new TwilioApiException(TwilioHttp.FormatError((int)response.StatusCode, payload));
        }

        return ParseMessage(payload);
    }

    private async Task<TwilioMessageResult> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, MessageInstancePath(messageSid));
        request.Content = new FormUrlEncodedContent(fields);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio update message {MessageSid} failed with HTTP {StatusCode}.", messageSid, (int)response.StatusCode);
            throw new TwilioApiException(TwilioHttp.FormatError((int)response.StatusCode, payload));
        }

        return ParseMessage(payload);
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativeOrNextPage)
    {
        var request = new HttpRequestMessage(method, NormalizeMessagingPath(relativeOrNextPage));
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_settings.AccountSid, _settings.AuthToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string MessagesListPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private string? ResolveNextPage(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        return NormalizeMessagingPath(nextPageUri);
    }

    private static string NormalizeMessagingPath(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return uriOrPath.TrimStart('/');
    }

    private static TwilioMessageResult ParseMessage(string payload)
    {
        var dto = JsonSerializer.Deserialize<MessageDto>(payload, TwilioHttp.JsonOptions)
            ?? throw new TwilioApiException("Twilio returned an empty message payload.");
        return ToResult(dto);
    }

    private static TwilioMessageResult ToResult(MessageDto dto)
    {
        return new TwilioMessageResult(
            dto.Sid,
            dto.Status ?? "unknown",
            dto.Body,
            ErrorCodeToString(dto.ErrorCode),
            TwilioHttp.Sanitize(dto.ErrorMessage),
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated),
            dto.From,
            dto.To);
    }

    private static string? ErrorCodeToString(JsonElement errorCode)
    {
        if (errorCode.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return errorCode.ToString();
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class MessageListDto
    {
        [JsonPropertyName("messages")]
        public List<MessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageDto
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }
    }
}
