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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio Programmable Messaging API (2010-04-01 Messages resource).
/// Create: POST /2010-04-01/Accounts/{AccountSid}/Messages.json
/// Fetch:  GET  /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
/// Update (cancel Status=canceled, redact Body=): POST .../Messages/{Sid}.json
/// List:   GET  .../Messages.json?From={FromNumber}&amp;DateSent&gt;={from}&amp;DateSent&lt;={to}
/// Confirmed: https://www.twilio.com/docs/messaging/api/message-resource
/// Scheduling: https://www.twilio.com/docs/messaging/features/message-scheduling
/// Redaction:  https://www.twilio.com/docs/messaging/tutorials/how-to-retrieve-and-modify-message-history
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly Uri _baseUri;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        TwilioLookupClient.ApplyBasicAuth(_httpClient, _settings);
        _baseUri = ResolveBaseUri(_settings.BaseUrl);
        _httpClient.BaseAddress ??= _baseUri;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioMessageRecord> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            // MessagingServiceSid is required to schedule. Confirmed:
            // https://www.twilio.com/docs/messaging/features/message-scheduling
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _httpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageRecord?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstancePath(messageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageRecord> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("Status", "canceled")
        });
        using var response = await _httpClient.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);
        return await ReadRequiredMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessageRecord> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Twilio redacts by updating Body to an empty string. Confirmed:
        // https://www.twilio.com/docs/messaging/tutorials/how-to-retrieve-and-modify-message-history
        using var content = new ByteArrayContent(Encoding.ASCII.GetBytes("Body="));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        using var response = await _httpClient.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);

        var accepted = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Conflict;
        if (!accepted)
        {
            return await ReadRequiredMessageAsync(response, cancellationToken);
        }

        TwilioMessageRecord? stored = null;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            stored = await FetchAsync(messageSid, cancellationToken);
            if (stored != null && string.IsNullOrEmpty(stored.Body))
            {
                return stored;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
        }

        if (stored == null)
        {
            throw new HttpRequestException("Twilio message was not found after content disposal.");
        }

        return stored;
    }

    public async Task<IReadOnlyList<TwilioMessageRecord>> ListFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageRecord>();
        var fromUtc = from.ToUniversalTime().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.ToUniversalTime().UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
                   $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}" +
                   $"&DateSent%3C={Uri.EscapeDataString(toUtc)}" +
                   "&PageSize=1000";

        while (!string.IsNullOrEmpty(path))
        {
            var requestUri = CombineWithBase(path);
            using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Twilio list messages failed with HTTP {(int)response.StatusCode}.");
            }

            var page = JsonSerializer.Deserialize<MessageListDto>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToRecord(message));
                }
            }

            path = string.IsNullOrEmpty(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return results;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private Uri CombineWithBase(string pathOrUri)
    {
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            // Keep using the configured messaging base, even if Twilio returned api.twilio.com.
            var relative = absolute.PathAndQuery.TrimStart('/');
            return new Uri(_baseUri, relative);
        }

        return new Uri(_baseUri, pathOrUri.TrimStart('/'));
    }

    private static Uri ResolveBaseUri(string? baseUrl)
    {
        var raw = string.IsNullOrWhiteSpace(baseUrl) ? DefaultMessagingBaseUrl : baseUrl.Trim();
        if (!raw.EndsWith("/", StringComparison.Ordinal))
        {
            raw += "/";
        }

        return new Uri(raw, UriKind.Absolute);
    }

    private async Task<TwilioMessageRecord> ReadRequiredMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            throw new HttpRequestException($"Twilio messaging call failed with HTTP {(int)response.StatusCode} code {error}.");
        }

        var dto = JsonSerializer.Deserialize<MessageDto>(payload, JsonOptions);
        if (dto == null)
        {
            throw new HttpRequestException("Twilio returned an empty messaging response.");
        }

        return ToRecord(dto);
    }

    private static int? TryReadError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            // Ignore parse failures; callers already treat this as a failed send.
        }

        return null;
    }

    private static TwilioMessageRecord ToRecord(MessageDto dto) => new()
    {
        Sid = dto.Sid,
        Status = dto.Status,
        ErrorCode = dto.ErrorCode,
        ErrorMessage = dto.ErrorMessage,
        Body = dto.Body,
        From = dto.From,
        To = dto.To,
        DateSent = dto.DateSent,
        DateCreated = dto.DateCreated
    };

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

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }
}
