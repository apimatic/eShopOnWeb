using System;
using System.Collections.Generic;
using System.Globalization;
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

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<TwilioMessageSnapshot> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = ImmediateMessageFields(to, body);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio MessagingServiceSid must be configured to schedule messages.");
        }

        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, MessagePath(messageSid));
        ApplyBasicAuth(request);
        return await SendAndParseMessageAsync(request, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        TwilioApiException? lastError = null;

        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, MessagePath(messageSid))
            {
                Content = new StringContent("Body=", Encoding.UTF8, "application/x-www-form-urlencoded")
            };
            ApplyBasicAuth(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var providerCode = ReadTwilioErrorCode(payload);

            if ((int)response.StatusCode == 409 || providerCode == 20409)
            {
                lastError = new TwilioApiException(
                    "Message is not yet in a modifiable state for content disposal.",
                    response.StatusCode,
                    20409);
                continue;
            }

            EnsureSuccess(response, payload);
            var parsed = JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions)
                ?? throw new TwilioApiException("Messaging API returned an empty message resource.");

            if (string.IsNullOrEmpty(parsed.Body))
            {
                return ToSnapshot(parsed);
            }

            var fetched = await FetchAsync(messageSid, cancellationToken);
            if (string.IsNullOrEmpty(fetched.Body))
            {
                return fetched;
            }

            lastError = new TwilioApiException("The provider still returns message content after redaction.");
        }

        throw lastError ?? new TwilioApiException("Could not dispose of message content at the provider.");
    }

    private static int? ReadTwilioErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio FromNumber must be configured.");
        }

        var fromSent = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toSent = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var query = string.Join("&",
            "From=" + Uri.EscapeDataString(_settings.FromNumber),
            "DateSent>=" + Uri.EscapeDataString(fromSent),
            "DateSent<=" + Uri.EscapeDataString(toSent),
            "PageSize=1000");
        var results = new List<TwilioMessageSnapshot>();
        var pages = 0;

        while (!string.IsNullOrEmpty(query) && pages < 100)
        {
            pages++;
            var url = new Uri(_httpClient.BaseAddress!, MessagesCollectionPath() + "?" + query);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            ApplyBasicAuth(request);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions)
                ?? throw new TwilioApiException("Message list returned an empty response.");

            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            query = ExtractQuery(page.NextPageUri);
        }

        return results;
    }

    private Dictionary<string, string> ImmediateMessageFields(string to, string body)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = RequiredFromNumber(),
            ["Body"] = body
        };

        return fields;
    }

    private async Task<TwilioMessageSnapshot> CreateMessageAsync(
        IDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(request);
        return await SendAndParseMessageAsync(request, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> UpdateMessageAsync(
        string messageSid,
        IDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagePath(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(request);
        return await SendAndParseMessageAsync(request, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> SendAndParseMessageAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, payload);

        var parsed = JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions)
            ?? throw new TwilioApiException("Messaging API returned an empty message resource.");

        return ToSnapshot(parsed);
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }
    }

    private string RequiredFromNumber()
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio FromNumber must be configured.");
        }

        return _settings.FromNumber;
    }

    private string MessagesCollectionPath() => $"Accounts/{_settings.AccountSid}/Messages.json";

    private string MessagePath(string messageSid) => $"Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var sanitized = TwilioApiException.Sanitize(payload);
        throw new TwilioApiException(
            $"Messaging API request failed with HTTP {(int)response.StatusCode}. {sanitized}",
            response.StatusCode);
    }

    private static string? ExtractQuery(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 && queryIndex < nextPageUri.Length - 1
            ? nextPageUri[(queryIndex + 1)..]
            : null;
    }

    private static TwilioMessageSnapshot ToSnapshot(TwilioMessageDto dto)
    {
        return new TwilioMessageSnapshot(
            dto.Sid ?? string.Empty,
            dto.Status ?? string.Empty,
            dto.Body,
            dto.From,
            dto.To,
            ReadErrorCode(dto.ErrorCode),
            TwilioApiException.Sanitize(dto.ErrorMessage),
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated),
            ParseTwilioDate(dto.DateUpdated));
    }

    private static string? ReadErrorCode(JsonElement errorCode)
    {
        return errorCode.ValueKind switch
        {
            JsonValueKind.Number => errorCode.GetRawText(),
            JsonValueKind.String => errorCode.GetString(),
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
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

    private sealed class MessageListResponse
    {
        public List<TwilioMessageDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_updated")]
        public string? DateUpdated { get; set; }
    }
}
