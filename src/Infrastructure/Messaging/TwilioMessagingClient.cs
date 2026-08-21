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
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";
    private const int PageSize = 1000;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public Task<TwilioMessageSnapshot> SendAsync(TwilioSendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = _settings.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        if (request.SendAt is not null)
        {
            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        return PostMessageAsync(MessagesCollectionUri(), fields, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstanceUri(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostMessageAsync(MessageInstanceUri(messageSid), fields, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string> { ["Body"] = string.Empty };
        return PostMessageAsync(MessageInstanceUri(messageSid), fields, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var firstPath =
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json" +
            $"?From={Uri.EscapeDataString(fromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromIso)}" +
            $"&{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toIso)}" +
            $"&PageSize={PageSize}";

        var results = new List<TwilioMessageSnapshot>();
        var next = firstPath;

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await _httpClient.GetAsync(ResolveMessagingUri(next), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateApiException(response, payload);
            }

            var page = JsonSerializer.Deserialize<MessageListJson>(payload, JsonOptions)
                ?? new MessageListJson();
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var snapshot = ToSnapshot(message);
                    if (snapshot is not null)
                    {
                        results.Add(snapshot);
                    }
                }
            }

            next = page.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> PostMessageAsync(
        Uri uri,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(uri, content, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response, payload);
        }

        var json = JsonSerializer.Deserialize<MessageJson>(payload, JsonOptions);
        var snapshot = ToSnapshot(json);
        if (snapshot is null)
        {
            throw new TwilioApiException((int)response.StatusCode, null, "Twilio returned a message without a SID.");
        }

        return snapshot;
    }

    private TwilioApiException CreateApiException(HttpResponseMessage response, string payload)
    {
        var error = TryReadError(payload);
        var sanitized = PhoneNumberSanitizer.Redact(error?.Message);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Twilio request failed.";
        }

        _logger.LogWarning("Twilio messaging API returned HTTP {StatusCode} with provider code {ProviderCode}",
            (int)response.StatusCode, error?.Code);
        return new TwilioApiException((int)response.StatusCode, error?.Code?.ToString(), sanitized);
    }

    private Uri MessagesCollectionUri() =>
        ResolveMessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json");

    private Uri MessageInstanceUri(string messageSid) =>
        ResolveMessagingUri($"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        var root = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl.TrimEnd('/');
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri($"{root}{absolute.PathAndQuery}");
        }

        if (!uriOrPath.StartsWith('/'))
        {
            uriOrPath = "/" + uriOrPath;
        }

        return new Uri(root + uriOrPath);
    }

    private static TwilioMessageSnapshot? ToSnapshot(MessageJson? json)
    {
        if (json is null || string.IsNullOrWhiteSpace(json.Sid))
        {
            return null;
        }

        return new TwilioMessageSnapshot(
            json.Sid,
            json.Status ?? "unknown",
            json.Body,
            ReadErrorCode(json.ErrorCode),
            json.ErrorMessage is null ? null : PhoneNumberSanitizer.Redact(json.ErrorMessage),
            ParseTwilioDate(json.DateSent),
            ParseTwilioDate(json.DateCreated),
            json.From);
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

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static TwilioLookupClient.TwilioErrorJson? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioLookupClient.TwilioErrorJson>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class MessageJson
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
    }

    private sealed class MessageListJson
    {
        [JsonPropertyName("messages")]
        public List<MessageJson>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
