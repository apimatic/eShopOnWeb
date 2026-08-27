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
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio messaging provider over plain HTTPS.
/// Messaging API contract (verified against https://www.twilio.com/docs/messaging/api/message-resource
/// and https://www.twilio.com/docs/messaging/features/message-scheduling):
///  - Send:            POST {base}/2010-04-01/Accounts/{AccountSid}/Messages.json (form: To, From|MessagingServiceSid, Body[, ScheduleType=fixed, SendAt])
///  - Fetch:           GET  {base}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
///  - Cancel schedule: POST {base}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json (form: Status=canceled)
///  - Redact content:  POST {base}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json (form: Body=)
///  - List:            GET  {base}/2010-04-01/Accounts/{AccountSid}/Messages.json?From=...&DateSent>=...&DateSent<=... (paged via next_page_uri)
/// Number validation uses the Lookup API (https://www.twilio.com/docs/lookup/v2-api), served from
/// lookups.twilio.com, which Twilio:BaseUrl does not govern.
/// Auth is HTTP Basic with AccountSid:AuthToken. The auth token and phone numbers are never logged.
/// </summary>
public class TwilioMessageProvider : IMessageProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessageProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessageProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessageProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber)
    {
        // Lookup API v2 - separate host, not governed by Twilio:BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidation(false, null, new[] { "NOT_A_NUMBER" });
        }
        await EnsureSuccess(response);

        var payload = await Deserialize<LookupResponse>(response);
        return new PhoneNumberValidation(
            payload?.Valid == true,
            payload?.PhoneNumber,
            payload?.ValidationErrors ?? Array.Empty<string>());
    }

    public async Task<ProviderMessage> SendMessageAsync(string toNumber, string body, DateTimeOffset? sendAtUtc = null)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toNumber),
            new("Body", body)
        };

        if (sendAtUtc.HasValue)
        {
            // Scheduling requires a Messaging Service; keep the configured sending number as the sender.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("From", _settings.FromNumber));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }
        else
        {
            form.Add(new("From", _settings.FromNumber));
        }

        using var response = await _httpClient.PostAsync(MessagesUrl(), new FormUrlEncodedContent(form));
        await EnsureSuccess(response);
        var message = await Deserialize<MessageResponse>(response);
        return ToProviderMessage(message!);
    }

    public async Task<ProviderMessage?> GetMessageAsync(string providerMessageSid)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccess(response);
        var message = await Deserialize<MessageResponse>(response);
        return ToProviderMessage(message!);
    }

    public async Task<ProviderMessage?> CancelScheduledMessageAsync(string providerMessageSid)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), new FormUrlEncodedContent(form));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccess(response);
        var message = await Deserialize<MessageResponse>(response);
        return ToProviderMessage(message!);
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid)
    {
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), new FormUrlEncodedContent(form));
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at the provider; the local record is redacted by the caller regardless.
            return;
        }
        await EnsureSuccess(response);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        // Ask the provider for only this application's own sending number's messages.
        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent>={Uri.EscapeDataString(fromUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                    $"&DateSent<={Uri.EscapeDataString(toUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}" +
                    "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUri = $"{MessagesUrl()}?{query}";
        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri);
            await EnsureSuccess(response);
            var page = await Deserialize<MessageListResponse>(response);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToProviderMessage));
            }
            nextUri = string.IsNullOrEmpty(page?.NextPageUri) ? null : _messagingBaseUrl + page!.NextPageUri;
        }
        return results;
    }

    private string MessagesUrl() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private static ProviderMessage ToProviderMessage(MessageResponse message) =>
        new(message.Sid ?? string.Empty,
            message.Status ?? string.Empty,
            message.To,
            message.From,
            TryParseDate(message.DateSent),
            message.ErrorCode,
            message.ErrorMessage);

    private static DateTimeOffset? TryParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        var message = response.ReasonPhrase ?? "Unknown error";
        try
        {
            var error = await Deserialize<TwilioErrorResponse>(response);
            code = error?.Code;
            if (!string.IsNullOrEmpty(error?.Message))
            {
                message = error.Message;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not parse Twilio error body");
        }
        throw new TwilioApiException(response.StatusCode, code, message);
    }

    private static async Task<T?> Deserialize<T>(HttpResponseMessage response) =>
        await JsonSerializer.DeserializeAsync<T>(await response.Content.ReadAsStreamAsync(), JsonOptions);

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private sealed class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
    }

    private sealed class MessageListResponse
    {
        [JsonPropertyName("messages")] public List<MessageResponse>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
