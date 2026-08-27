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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written Twilio messaging client built against the authoritative OpenAPI
/// specification in api-specs/twilio/twilio_api_v2010 (Messages resource):
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages.json        CreateMessage
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages.json        ListMessage
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  FetchMessage
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  UpdateMessage (redact Body / cancel Status)
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// Never logs recipient numbers, message bodies or credentials.
/// </summary>
public class TwilioMessageProvider : IMessageProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;

    public TwilioMessageProvider(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;
        _baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? DefaultBaseUrl : settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default)
    {
        // CreateMessage: application/x-www-form-urlencoded per the spec's requestBody.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber),
        };

        if (scheduleAt.HasValue)
        {
            // Message scheduling is a Messaging Services feature: ScheduleType=fixed
            // with SendAt in ISO 8601, per the spec's ScheduleType/SendAt parameters.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", scheduleAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesUri(), new FormUrlEncodedContent(form), cancellationToken);
        var message = await ReadResponseAsync<TwilioMessageDto>(response, cancellationToken);
        return ToProviderMessage(message!);
    }

    public async Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUri(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var message = await ReadResponseAsync<TwilioMessageDto>(response, cancellationToken);
        return ToProviderMessage(message!);
    }

    public async Task<ProviderMessage?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with Status=canceled (message_enum_update_status).
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUri(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        var message = await ReadResponseAsync<TwilioMessageDto>(response, cancellationToken);
        return ToProviderMessage(message!);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // UpdateMessage with Body="" redacts the text content at the provider while
        // the Message resource (and its delivery outcome) survives.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUri(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Provider error 20409: a redaction for this message is already queued and
            // will complete once the message is finalized - the desired end state.
            return;
        }
        await ReadResponseAsync<TwilioMessageDto>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // ListMessage filtered server-side by sender (From) and sent-date range, so only
        // this application's own traffic is returned. DateSent< is exclusive at the day
        // boundary, so add one day to cover the whole 'to' date.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", fromNumber),
            new("DateSent>", from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("PageSize", "1000"),
        };

        var results = new List<ProviderMessage>();
        string? nextUri = MessagesUri() + "?" + string.Join("&", query.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

        while (!string.IsNullOrEmpty(nextUri))
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadResponseAsync<TwilioListMessagesResponse>(response, cancellationToken);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToProviderMessage));
            }
            nextUri = string.IsNullOrEmpty(page?.NextPageUri) ? null : _baseUrl + page!.NextPageUri;
        }

        return results;
    }

    private string MessagesUri() => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUri(string messageSid) => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static async Task<T?> ReadResponseAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response, content);
        }
        return JsonSerializer.Deserialize<T>(content);
    }

    private static MessageProviderException ToException(HttpResponseMessage response, string content)
    {
        // Twilio error model: { "code": 21211, "message": "...", "more_info": "...", "status": 400 }
        // The provider's message text may embed the destination number; it is kept in
        // ProviderDetail for storage but never put into Exception.Message (logs).
        int? code = null;
        string? detail = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
            }
            if (doc.RootElement.TryGetProperty("message", out var msgEl))
            {
                detail = msgEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with status only.
        }
        return new MessageProviderException((int)response.StatusCode, code, detail);
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageDto dto) => new(
        dto.Sid ?? string.Empty,
        dto.Status,
        dto.To,
        dto.From,
        dto.Body,
        dto.ErrorCode,
        dto.ErrorMessage,
        ParseRfc2822(dto.DateCreated),
        ParseRfc2822(dto.DateSent));

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // The spec types these as date-time-rfc-2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private sealed class TwilioMessageDto
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class TwilioListMessagesResponse
    {
        [JsonPropertyName("messages")] public List<TwilioMessageDto>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }
}
