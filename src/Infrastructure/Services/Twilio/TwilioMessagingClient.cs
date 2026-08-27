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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Speaks to the provider's classic messaging API (api.twilio.com /2010-04-01), or to the
/// configured Twilio:BaseUrl override for every call. Requests are form-encoded and
/// authenticated with HTTP Basic (Account SID + Auth Token).
/// </summary>
public class TwilioMessagingClient : ISmsMessagingClient
{
    private const int ListPageSize = 1000;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settings.Validate();

        _httpClient.BaseAddress = new Uri(_settings.MessagingBaseUrl + "/");
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber!),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            // Scheduling is a Messaging Services feature: ScheduleType=fixed plus an
            // ISO 8601 SendAt, 15 minutes to 35 days in the future.
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await PostFormAsync(MessagesUrl(), form, cancellationToken);
        var message = await ReadMessageAsync(response, cancellationToken);

        return new SmsSendResult
        {
            MessageSid = message.Sid ?? string.Empty,
            Status = message.Status ?? string.Empty
        };
    }

    public async Task<SmsMessageDetails?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var message = await ReadMessageAsync(response, cancellationToken);
        return ToDetails(message);
    }

    public async Task<SmsMessageDetails?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // "canceled" is the only value the Status parameter accepts, and it only
        // applies to messages that have not been sent yet.
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await PostFormAsync(MessageUrl(messageSid), form, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var message = await ReadMessageAsync(response, cancellationToken);
        return ToDetails(message);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body value redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await PostFormAsync(MessageUrl(messageSid), form, cancellationToken);
        await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderSmsMessage>> ListMessagesFromSendingNumberAsync(CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only,
        // and follow every page so the whole range is covered.
        var results = new List<ProviderSmsMessage>();
        var nextUri = $"{MessagesUrl()}?From={Uri.EscapeDataString(_settings.FromNumber!)}&PageSize={ListPageSize}";

        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadAsync<MessageListPage>(response, cancellationToken);

            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(m => new ProviderSmsMessage
                {
                    MessageSid = m.Sid ?? string.Empty,
                    To = m.To,
                    From = m.From,
                    Status = m.Status ?? string.Empty,
                    ErrorCode = m.ErrorCode,
                    DateCreated = ParseRfc2822(m.DateCreated),
                    DateSent = ParseRfc2822(m.DateSent)
                }));
            }

            nextUri = string.IsNullOrEmpty(page?.NextPageUri) ? null : page!.NextPageUri;
        }

        return results;
    }

    private string MessagesUrl() => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string messageSid) => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private Task<HttpResponseMessage> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        return _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
    }

    private async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = await ReadAsync<TwilioMessage>(response, cancellationToken);
        return message ?? new TwilioMessage();
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            int? errorCode = null;
            var errorMessage = $"Provider request failed with status {(int)response.StatusCode}.";
            try
            {
                var error = JsonSerializer.Deserialize<TwilioError>(content, JsonOptions);
                if (error != null)
                {
                    errorCode = error.Code;
                    if (!string.IsNullOrWhiteSpace(error.Message))
                    {
                        errorMessage = error.Message;
                    }
                }
            }
            catch (JsonException)
            {
                // keep the generic message
            }

            throw new TwilioApiException(response.StatusCode, errorCode, errorMessage);
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static SmsMessageDetails ToDetails(TwilioMessage message) => new()
    {
        MessageSid = message.Sid ?? string.Empty,
        Status = message.Status ?? string.Empty,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        DateCreated = ParseRfc2822(message.DateCreated),
        DateSent = ParseRfc2822(message.DateSent)
    };

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private sealed class TwilioMessage
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
    }

    private sealed class MessageListPage
    {
        [JsonPropertyName("messages")] public List<TwilioMessage>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
