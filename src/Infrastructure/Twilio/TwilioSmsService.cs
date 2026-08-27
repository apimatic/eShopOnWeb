using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed SMS service. Uses the Messaging API (api.twilio.com, or the
/// configured BaseUrl override) for sending, scheduling, cancelling, redacting
/// and reconciling messages, and the Lookup API (lookups.twilio.com) for
/// validating and canonicalizing phone numbers.
/// Phone numbers and credentials are never written to logs.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;
    private readonly string _messagesUri;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrEmpty(_settings.AccountSid) || string.IsNullOrEmpty(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured. Set the Twilio:AccountSid and Twilio:AuthToken configuration values.");
        }

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        var messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');
        _messagesUri = $"{messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages";
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup is served from its own host; Twilio:BaseUrl does not govern it.
        var lookupUri = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        var response = await _httpClient.GetAsync(lookupUri, cancellationToken);

        if ((int)response.StatusCode == 404)
        {
            return new PhoneNumberValidationResult { IsValid = false, ValidationErrors = new[] { "not a valid phone number" } };
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var lookup = JsonSerializer.Deserialize<LookupResponse>(payload, JsonOptions);

        return new PhoneNumberValidationResult
        {
            IsValid = lookup?.Valid == true,
            CanonicalNumber = lookup?.Valid == true ? lookup.PhoneNumber : null,
            ValidationErrors = lookup?.ValidationErrors ?? new List<string>()
        };
    }

    public async Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("To", to),
            new KeyValuePair<string, string>("Body", body)
        };

        if (sendAt.HasValue)
        {
            // Scheduled messages are a Messaging-Service-only capability.
            if (string.IsNullOrEmpty(_settings.MessagingServiceSid))
            {
                return SmsSendResult.Failed("Scheduling requires Twilio:MessagingServiceSid to be configured.");
            }
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", sendAt.Value.UtcDateTime.ToString("o")));
        }
        else
        {
            fields.Add(new KeyValuePair<string, string>("From", _settings.FromNumber));
        }

        var response = await _httpClient.PostAsync($"{_messagesUri}.json", new FormUrlEncodedContent(fields), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = JsonSerializer.Deserialize<TwilioErrorResponse>(payload, JsonOptions);
            // The provider's error message can embed the destination number; log the code only.
            _logger.LogWarning("Twilio rejected a message with error code {ErrorCode}", error?.Code);
            return SmsSendResult.Failed(error?.Message ?? $"The provider returned status {(int)response.StatusCode}.");
        }

        var message = JsonSerializer.Deserialize<MessageResponse>(payload, JsonOptions);
        return SmsSendResult.Sent(message!.Sid!, message.Status ?? "queued");
    }

    public async Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Status", "canceled")
        };

        var response = await _httpClient.PostAsync($"{_messagesUri}/{messageSid}.json", new FormUrlEncodedContent(fields), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio could not cancel scheduled message {MessageSid}: status {StatusCode}", messageSid, (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redacts the body at the provider while keeping the rest of the
        // Message resource (including its delivery outcome) intact.
        var fields = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>("Body", string.Empty)
        };

        var response = await _httpClient.PostAsync($"{_messagesUri}/{messageSid}.json", new FormUrlEncodedContent(fields), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio could not redact message {MessageSid}: status {StatusCode}", messageSid, (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<SmsMessageRecord?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"{_messagesUri}/{messageSid}.json", cancellationToken);
        if ((int)response.StatusCode == 404)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = JsonSerializer.Deserialize<MessageResponse>(payload, JsonOptions);
        return ToRecord(message!);
    }

    public async Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's
        // messages (provider-side From filter), covering the whole range by
        // following pagination.
        var results = new List<SmsMessageRecord>();
        var fromText = from.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var toText = to.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        var nextUri = $"{_messagesUri}.json?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                      $"&DateSent%3E={Uri.EscapeDataString(fromText)}&DateSent%3C={Uri.EscapeDataString(toText)}&PageSize=1000";

        while (nextUri != null)
        {
            var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions);

            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToRecord(message));
                }
            }

            nextUri = !string.IsNullOrEmpty(page?.NextPageUri)
                ? $"{GetMessagingBase()}{page.NextPageUri}"
                : null;
        }

        return results;
    }

    private string GetMessagingBase()
    {
        var index = _messagesUri.IndexOf("/2010-04-01", StringComparison.Ordinal);
        return index > 0 ? _messagesUri.Substring(0, index) : DefaultMessagingBaseUrl;
    }

    private static SmsMessageRecord ToRecord(MessageResponse message)
    {
        return new SmsMessageRecord
        {
            Sid = message.Sid ?? string.Empty,
            To = message.To,
            From = message.From,
            Status = message.Status,
            DateSent = ParseTwilioDate(message.DateSent),
            DateCreated = ParseTwilioDate(message.DateCreated)
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        // Twilio returns RFC 2822 dates, e.g. "Wed, 19 Jun 2019 22:04:00 +0000".
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private class LookupResponse
    {
        [JsonPropertyName("phone_number")]
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("validation_errors")]
        public List<string>? ValidationErrors { get; set; }
    }

    private class MessageResponse
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }
    }

    private class MessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<MessageResponse>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private class TwilioErrorResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
