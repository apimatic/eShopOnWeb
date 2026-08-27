using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio REST client. Talks to the messaging API (api.twilio.com, or the
/// configured Twilio:BaseUrl override) for sending/reading messages, and to the
/// Lookup API (lookups.twilio.com) for phone number validation. Authenticates
/// with HTTP Basic (AccountSid:AuthToken). Credentials and destination numbers
/// are never logged.
/// </summary>
public class TwilioGateway : ISmsGateway, IPhoneNumberLookup
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioGateway> _logger;

    public TwilioGateway(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    private string MessageUrl(string messageSid) => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";

    public async Task<SmsSendResult> SendAsync(string to, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", body),
        };

        if (sendAtUtc.HasValue)
        {
            // Provider-side scheduling requires a Messaging Service and an ISO 8601 SendAt.
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesUrl, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (errorCode, errorMessage) = ParseError(payload);
            _logger.LogWarning("Twilio rejected a message send with HTTP {StatusCode}, error {ErrorCode}", (int)response.StatusCode, errorCode);
            return new SmsSendResult(false, null, null, errorCode, errorMessage);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        return new SmsSendResult(true,
            root.GetProperty("sid").GetString(),
            root.TryGetProperty("status", out var status) ? status.GetString() : null,
            null, null);
    }

    public async Task<SmsMessageStatus?> GetMessageStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio message fetch for {MessageSid} returned HTTP {StatusCode}", messageSid, (int)response.StatusCode);
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        return new SmsMessageStatus(
            root.TryGetProperty("status", out var status) ? status.GetString() ?? "unknown" : "unknown",
            root.TryGetProperty("error_code", out var errorCode) && errorCode.ValueKind == JsonValueKind.Number ? errorCode.GetInt32() : null,
            root.TryGetProperty("error_message", out var errorMessage) ? errorMessage.GetString() : null);
    }

    public async Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Status=canceled is the only value the update endpoint accepts for cancellation.
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio schedule cancel for {MessageSid} returned HTTP {StatusCode}", messageSid, (int)response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio body redaction for {MessageSid} returned HTTP {StatusCode}", messageSid, (int)response.StatusCode);
        }
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's messages,
        // date-bounded (the DateSent filters are date-granular, GMT); the precise
        // date-time window is then applied to the returned records.
        // DateSent filters are date-granular and the upper bound is exclusive of
        // the given day, so the upper date is the day after `toUtc`.
        var query = $"{MessagesUrl}?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={fromUtc.UtcDateTime:yyyy-MM-dd}" +
            $"&DateSent%3C={toUtc.UtcDateTime.Date.AddDays(1):yyyy-MM-dd}" +
            "&PageSize=1000";

        var records = new List<SmsMessageRecord>();
        string? nextUri = query;
        while (nextUri is not null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;

            foreach (var message in root.GetProperty("messages").EnumerateArray())
            {
                var dateSent = ParseRfc2822(message, "date_sent");
                var dateCreated = ParseRfc2822(message, "date_created");
                var effectiveDate = dateSent ?? dateCreated;
                if (effectiveDate is null || effectiveDate < fromUtc || effectiveDate > toUtc)
                {
                    continue;
                }

                records.Add(new SmsMessageRecord(
                    message.GetProperty("sid").GetString()!,
                    message.TryGetProperty("to", out var to) ? to.GetString() : null,
                    message.TryGetProperty("status", out var status) ? status.GetString() : null,
                    dateSent, dateCreated));
            }

            nextUri = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? $"{MessagingBaseUrl}{next.GetString()}"
                : null;
        }

        return records;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (_, errorMessage) = ParseError(payload);
            _logger.LogWarning("Twilio Lookup returned HTTP {StatusCode} for a registration attempt", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null, errorMessage ?? $"Lookup failed with HTTP {(int)response.StatusCode}");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var isValid = root.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var number) ? number.GetString() : null;

        if (!isValid || canonical is null)
        {
            var errors = root.TryGetProperty("validation_errors", out var validationErrors)
                ? validationErrors.ToString()
                : "not a usable destination";
            return new PhoneNumberLookupResult(false, null, $"Phone number is not valid: {errors}");
        }

        return new PhoneNumberLookupResult(true, canonical, null);
    }

    private static DateTimeOffset? ParseRfc2822(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static (int? Code, string? Message) ParseError(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                ? codeElement.GetInt32()
                : (int?)null;
            var message = root.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
