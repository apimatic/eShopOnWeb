using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed SMS service over plain HTTPS.
/// Messaging API contract (verified against Twilio's official docs):
///   Create message:        POST {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json  (To, From, Body)
///   Schedule message:      same, plus ScheduleType=fixed, SendAt (ISO-8601 UTC), MessagingServiceSid
///   Cancel scheduled:      POST .../Messages/{Sid}.json  (Status=canceled)
///   Redact message body:   POST .../Messages/{Sid}.json  (Body= empty string)
///   Fetch message:         GET  .../Messages/{Sid}.json
///   List messages:         GET  .../Messages.json?From=...&amp;DateSent&gt;=...&amp;DateSent&lt;=... (paged via next_page_uri)
/// Number validation uses the Lookup API (lookups.twilio.com), which is a separate host
/// and is NOT governed by the Twilio:BaseUrl override.
/// Phone numbers and message bodies are never written to logs.
/// </summary>
public class TwilioSmsService : ISmsService
{
    public const string MessagingHttpClientName = "TwilioMessaging";
    public const string LookupHttpClientName = "TwilioLookup";

    public const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    public const string LookupBaseUrl = "https://lookups.twilio.com";

    // Twilio error code returned when cancelling a message that is no longer scheduled.
    private const int MessageNotScheduledErrorCode = 30409;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(LookupHttpClientName);
        var response = await client.GetAsync($"v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}", cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, "The phone number is not a usable destination.");
        }

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var isValid = root.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var phoneProp) && phoneProp.ValueKind == JsonValueKind.String
            ? phoneProp.GetString()
            : null;

        if (!isValid || string.IsNullOrEmpty(canonical))
        {
            var errors = root.TryGetProperty("validation_errors", out var errProp) && errProp.ValueKind == JsonValueKind.Array
                ? string.Join(",", errProp.EnumerateArray())
                : null;
            return new PhoneNumberValidationResult(false, null, errors ?? "The phone number is not a usable destination.");
        }

        return new PhoneNumberValidationResult(true, canonical, null);
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", RequireSetting(_settings.FromNumber, nameof(_settings.FromNumber))),
            new("Body", body)
        };
        return PostMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("MessagingServiceSid", RequireSetting(_settings.MessagingServiceSid, nameof(_settings.MessagingServiceSid))),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
        };
        return PostMessageAsync(form, cancellationToken);
    }

    public async Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = CreateMessagingClient();
        var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Status", "canceled") });
        var response = await client.PostAsync(MessageUri(messageSid), form, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var (errorCode, _) = await ReadErrorAsync(response, cancellationToken);
        if (errorCode == MessageNotScheduledErrorCode.ToString(CultureInfo.InvariantCulture))
        {
            _logger.LogWarning("Message {MessageSid} is no longer in a cancellable (scheduled) state.", messageSid);
            return false;
        }

        _logger.LogWarning("Cancelling message {MessageSid} failed with provider error {ErrorCode}.", messageSid, errorCode);
        return false;
    }

    public async Task<SmsMessageInfo?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = CreateMessagingClient();
        var response = await client.GetAsync(MessageUri(messageSid), cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseMessage(doc.RootElement);
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = CreateMessagingClient();
        // An empty Body redacts the text content at the provider while keeping the message record.
        var form = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Body", string.Empty) });
        var response = await client.PostAsync(MessageUri(messageSid), form, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (errorCode, _) = await ReadErrorAsync(response, cancellationToken);
            _logger.LogWarning("Redacting body of message {MessageSid} failed with provider error {ErrorCode}.", messageSid, errorCode);
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var client = CreateMessagingClient();
        var fromNumber = RequireSetting(_settings.FromNumber, nameof(_settings.FromNumber));

        // Ask the provider for this application's own sending number only.
        // DateSent comparisons are date-granular (UTC); the exact instants are refined below.
        var query = $"{MessagesUri()}?From={Uri.EscapeDataString(fromNumber)}" +
                    $"&DateSent%3E={from.UtcDateTime:yyyy-MM-dd}" +
                    $"&DateSent%3C={to.UtcDateTime:yyyy-MM-dd}" +
                    "&PageSize=100";

        var results = new List<SmsMessageInfo>();
        string? nextUri = query;
        while (nextUri is not null)
        {
            var response = await client.GetAsync(nextUri, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages))
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var info = ParseMessage(message);
                    var effectiveDate = info.DateSent;
                    if (effectiveDate is null || (effectiveDate >= from && effectiveDate <= to))
                    {
                        results.Add(info);
                    }
                }
            }

            nextUri = root.TryGetProperty("next_page_uri", out var nextProp) && nextProp.ValueKind == JsonValueKind.String
                ? nextProp.GetString()
                : null;
        }

        return results;
    }

    private async Task<SmsSendResult> PostMessageAsync(List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        var client = CreateMessagingClient();
        var response = await client.PostAsync(MessagesUri(), new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            using var errorDoc = JsonDocument.Parse(payload);
            var errorCode = errorDoc.RootElement.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            // Provider error messages can embed the destination number; they are stored/returned but never logged.
            var errorMessage = errorDoc.RootElement.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : response.ReasonPhrase;
            return new SmsSendResult(false, null, null, errorCode, errorMessage);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = root.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
        var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        return new SmsSendResult(true, sid, status, null, null);
    }

    private static SmsMessageInfo ParseMessage(JsonElement message)
    {
        string? GetString(string name) =>
            message.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;

        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString("date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new SmsMessageInfo(
            GetString("sid") ?? string.Empty,
            GetString("status") ?? string.Empty,
            GetString("to"),
            GetString("from"),
            dateSent,
            GetString("error_code"));
    }

    private static async Task<(string? ErrorCode, string? ErrorMessage)> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var code = doc.RootElement.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return (code, null);
        }
        catch (JsonException)
        {
            return (((int)response.StatusCode).ToString(CultureInfo.InvariantCulture), null);
        }
    }

    private HttpClient CreateMessagingClient() =>
        _httpClientFactory.CreateClient(MessagingHttpClientName);

    private string MessagesUri() => $"2010-04-01/Accounts/{RequireSetting(_settings.AccountSid, nameof(_settings.AccountSid))}/Messages.json";

    private string MessageUri(string messageSid) => $"2010-04-01/Accounts/{RequireSetting(_settings.AccountSid, nameof(_settings.AccountSid))}/Messages/{messageSid}.json";

    private static string RequireSetting(string? value, string name) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Twilio setting '{name}' is not configured.")
            : value;
}
