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
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string MessagingClientName = "TwilioMessaging";
    public const string LookupClientName = "TwilioLookup";
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(LookupClientName);
        var path = "v2/PhoneNumbers/" + Uri.EscapeDataString(phoneNumber);
        using var request = CreateAuthorizedRequest(HttpMethod.Get, path);
        using var response = await client.SendAsync(request, cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Lookup rejected the number with HTTP {StatusCode} Twilio code {TwilioCode}",
                (int)response.StatusCode, FormatErrorCode(ReadErrorCode(payload)));
            return new TwilioLookupResult(false, null, new[] { "lookup_failed" });
        }

        var dto = JsonSerializer.Deserialize<LookupResponseDto>(payload, JsonOptions);
        if (dto == null)
        {
            return new TwilioLookupResult(false, null, new[] { "lookup_failed" });
        }

        return new TwilioLookupResult(dto.Valid, dto.PhoneNumber, dto.ValidationErrors);
    }

    public Task<TwilioSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = CreateBaseMessageFields(to, body);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<TwilioSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = CreateBaseMessageFields(to, body);
        fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
        fields.Add(new KeyValuePair<string, string>("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot?> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var request = CreateAuthorizedRequest(HttpMethod.Get, MessageResourcePath(messageSid));
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch message {MessageSid} failed with HTTP {StatusCode} Twilio code {TwilioCode}",
                messageSid, (int)response.StatusCode, FormatErrorCode(ReadErrorCode(payload)));
            return null;
        }

        var dto = JsonSerializer.Deserialize<MessageResponseDto>(payload, JsonOptions);
        return dto == null ? null : ToSnapshot(dto);
    }

    public async Task<TwilioSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        return await UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task<TwilioSendResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };
        return await UpdateMessageAsync(messageSid, fields, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        var results = new List<TwilioMessageSnapshot>();

        var sender = _settings.FromNumber;
        var fromBound = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toBound = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var queryString =
            "From=" + Uri.EscapeDataString(sender) +
            "&PageSize=1000" +
            "&DateSent%3E=" + Uri.EscapeDataString(fromBound) +
            "&DateSent%3C=" + Uri.EscapeDataString(toBound);

        var nextPath = MessagesCollectionPath() + "?" + queryString;

        while (!string.IsNullOrEmpty(nextPath))
        {
            using var request = CreateAuthorizedRequest(HttpMethod.Get, nextPath.TrimStart('/'));
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("List messages failed with HTTP {StatusCode} Twilio code {TwilioCode}",
                    (int)response.StatusCode, FormatErrorCode(ReadErrorCode(payload)));
                break;
            }

            var page = JsonSerializer.Deserialize<MessageListResponseDto>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            nextPath = ToRelativeMessagingPath(page?.NextPageUri);
        }

        return results;
    }

    private List<KeyValuePair<string, string>> CreateBaseMessageFields(string to, string body)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        return fields;
    }

    private async Task<TwilioSendResult> CreateMessageAsync(
        List<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var request = CreateAuthorizedRequest(HttpMethod.Post, MessagesCollectionPath());
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = ReadErrorCode(payload);
            _logger.LogWarning("Create message failed with HTTP {StatusCode} Twilio code {TwilioCode}",
                (int)response.StatusCode, FormatErrorCode(errorCode));
            return new TwilioSendResult(false, null, "failed", errorCode);
        }

        var dto = JsonSerializer.Deserialize<MessageResponseDto>(payload, JsonOptions);
        if (dto == null || string.IsNullOrEmpty(dto.Sid))
        {
            return new TwilioSendResult(false, null, "failed", null);
        }

        _logger.LogInformation("Created message {MessageSid} with status {Status}", dto.Sid, dto.Status ?? "unknown");
        return new TwilioSendResult(true, dto.Sid, dto.Status ?? "queued", ReadNullableInt(dto.ErrorCode));
    }

    private async Task<TwilioSendResult> UpdateMessageAsync(
        string messageSid,
        List<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var request = CreateAuthorizedRequest(HttpMethod.Post, MessageResourcePath(messageSid));
        request.Content = new FormUrlEncodedContent(fields);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = ReadErrorCode(payload);
            _logger.LogWarning("Update message {MessageSid} failed with HTTP {StatusCode} Twilio code {TwilioCode}",
                messageSid, (int)response.StatusCode, FormatErrorCode(errorCode));
            return new TwilioSendResult(false, messageSid, "failed", errorCode);
        }

        var dto = JsonSerializer.Deserialize<MessageResponseDto>(payload, JsonOptions);
        if (dto == null)
        {
            return new TwilioSendResult(false, messageSid, "failed", null);
        }

        return new TwilioSendResult(true, dto.Sid, dto.Status ?? "updated", ReadNullableInt(dto.ErrorCode));
    }

    private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativePath)
    {
        var request = new HttpRequestMessage(method, relativePath);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageResourcePath(string messageSid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

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

    private static TwilioMessageSnapshot ToSnapshot(MessageResponseDto dto)
    {
        return new TwilioMessageSnapshot(
            dto.Sid ?? string.Empty,
            dto.Status ?? string.Empty,
            dto.Body,
            ReadNullableInt(dto.ErrorCode),
            ParseTwilioDate(dto.DateSent),
            ParseTwilioDate(dto.DateCreated));
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

    private static object FormatErrorCode(int? code) => code.HasValue ? code.Value : "none";

    private static int? ReadErrorCode(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
            {
                return code.GetInt32();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static int? ReadNullableInt(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String && int.TryParse(element.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class LookupResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("validation_errors")]
        public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponseDto
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }

    private sealed class MessageListResponseDto
    {
        [JsonPropertyName("messages")]
        public List<MessageResponseDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
