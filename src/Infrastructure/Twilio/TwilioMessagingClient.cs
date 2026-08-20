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
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ISmsMessageGateway
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
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<SmsDispatchResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = TryDeserializeMessage(payload);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio create-message returned {StatusCode} for account {AccountSid}.",
                (int)response.StatusCode,
                _settings.AccountSid);
            return new SmsDispatchResult(
                Accepted: false,
                ProviderMessageSid: message?.Sid,
                Status: message?.Status ?? "failed",
                ErrorCode: ErrorCodeToString(message?.ErrorCode) ?? response.StatusCode.ToString());
        }

        return new SmsDispatchResult(
            Accepted: true,
            ProviderMessageSid: message?.Sid,
            Status: message?.Status ?? "queued",
            ErrorCode: ErrorCodeToString(message?.ErrorCode));
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstancePath(providerMessageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        response.EnsureSuccessStatusCode();
        var message = DeserializeMessage(payload);
        return ToSnapshot(message);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Status", "canceled")
        });
        using var response = await _httpClient.PostAsync(MessageInstancePath(providerMessageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio cancel-message returned {StatusCode} for message {MessageSid}: {Message}",
                (int)response.StatusCode,
                providerMessageSid,
                LogSanitizer.RedactPhoneNumbers(payload));
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Body", string.Empty)
        });
        using var response = await _httpClient.PostAsync(MessageInstancePath(providerMessageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Twilio redact-message returned {StatusCode} for message {MessageSid}: {Message}",
                (int)response.StatusCode,
                providerMessageSid,
                LogSanitizer.RedactPhoneNumbers(payload));
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var relative = MessagesCollectionPath()
            + "?From=" + Uri.EscapeDataString(_settings.FromNumber)
            + "&DateSent%3E=" + Uri.EscapeDataString(fromIso)
            + "&DateSent%3C=" + Uri.EscapeDataString(toIso)
            + "&PageSize=1000";

        var pages = 0;
        while (!string.IsNullOrEmpty(relative) && pages < 100)
        {
            pages++;
            var requestUri = ResolveMessagingUri(relative);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            var page = JsonSerializer.Deserialize<TwilioMessageListDto>(payload, JsonOptions)
                ?? new TwilioMessageListDto();
            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToSnapshot));
            }

            relative = page.NextPageUri;
        }

        return results;
    }

    public static void ConfigureClient(HttpClient client, TwilioSettings settings)
    {
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultBaseUrl : settings.BaseUrl.TrimEnd('/');
        client.BaseAddress = new Uri(baseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(30);
        ApplyBasicAuth(client, settings);
    }

    public static void ApplyBasicAuth(HttpClient client, TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid) =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private Uri ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(_httpClient.BaseAddress!, absolute.PathAndQuery);
        }

        return new Uri(_httpClient.BaseAddress!, nextPageUri.TrimStart('/'));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static TwilioMessageDto? TryDeserializeMessage(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TwilioMessageDto DeserializeMessage(string payload) =>
        JsonSerializer.Deserialize<TwilioMessageDto>(payload, JsonOptions) ?? new TwilioMessageDto();

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageDto message)
    {
        return new SmsMessageSnapshot(
            Sid: message.Sid ?? string.Empty,
            Status: message.Status ?? string.Empty,
            Body: message.Body,
            From: message.From,
            DateSent: ParseTwilioDate(message.DateSent),
            DateCreated: ParseTwilioDate(message.DateCreated),
            ErrorCode: ErrorCodeToString(message.ErrorCode));
    }

    private static string? ErrorCodeToString(JsonElement errorCode)
    {
        if (errorCode.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return errorCode.ToString();
    }

    private static string? ErrorCodeToString(JsonElement? errorCode)
    {
        if (errorCode is null)
        {
            return null;
        }

        return ErrorCodeToString(errorCode.Value);
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

    private sealed class TwilioMessageListDto
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageDto> Messages { get; set; } = new();

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageDto
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("error_code")]
        public JsonElement ErrorCode { get; set; }
    }
}
