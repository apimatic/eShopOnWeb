using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio-backed SMS service. Talks to the messaging API (api.twilio.com, or the
/// configured BaseUrl override) for sending/reading/reconciling messages, and to
/// the Lookup API (lookups.twilio.com) for number validation. Authenticates with
/// HTTP Basic (Account SID : Auth Token). Phone numbers and the auth token are
/// never logged.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersionPath = "2010-04-01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagingBase => string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? DefaultMessagingBaseUrl
        : _settings.BaseUrl.TrimEnd('/');

    private string MessagesUrl =>
        $"{MessagingBase}/{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages.json";

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return new PhoneNumberValidationResult(false, null, "A phone number is required.");
        }

        // Lookup is served from its own host; the messaging BaseUrl override does not apply.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber.Trim())}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, "The phone number is not a usable destination.");
        }

        await EnsureSuccessAsync(response, cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var isValid = root.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var numberProp) ? numberProp.GetString() : null;

        if (!isValid || canonical is null)
        {
            var reasons = root.TryGetProperty("validation_errors", out var errorsProp)
                ? string.Join(", ", errorsProp.EnumerateArray().Select(e => e.GetString()))
                : "invalid number";
            return new PhoneNumberValidationResult(false, null, $"The phone number is not a usable destination ({reasons}).");
        }

        return new PhoneNumberValidationResult(true, canonical, null);
    }

    public async Task<SmsSendResult> SendAsync(string to, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        if (scheduleAt.HasValue)
        {
            // Scheduling is a Messaging Services capability: the provider holds
            // the message and sends it at SendAt.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", scheduleAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesUrl,
            new FormUrlEncodedContent(form), cancellationToken);

        return await ReadSendResultAsync(response, cancellationToken);
    }

    public async Task<SmsMessageInfo?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid),
            new FormUrlEncodedContent(form), cancellationToken);
        return await ReadSendResultAsync(response, cancellationToken);
    }

    public async Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body value disposes of the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid),
            new FormUrlEncodedContent(form), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<SmsMessageInfo>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages
        // only (From filter applied provider-side), covering the whole range.
        // The date filters are day-granular, so widen by a day on each side and
        // trim precisely against the message timestamps below.
        var query = new List<KeyValuePair<string, string>>
        {
            new("From", _settings.FromNumber),
            new("DateSent>", from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("DateSent<", to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new("PageSize", "1000")
        };

        var messages = new List<SmsMessageInfo>();
        string? nextUri = $"{MessagesUrl}?{BuildQuery(query)}";

        while (nextUri is not null)
        {
            var url = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : $"{MessagingBase}{nextUri}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            using var doc = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messagesProp))
            {
                foreach (var element in messagesProp.EnumerateArray())
                {
                    var message = ParseMessage(element);
                    var timestamp = message.DateSent ?? message.DateCreated;
                    if (timestamp.HasValue && timestamp.Value >= from && timestamp.Value <= to)
                    {
                        messages.Add(message);
                    }
                }
            }

            nextUri = root.TryGetProperty("next_page_uri", out var nextProp) ? nextProp.GetString() : null;
        }

        return messages;
    }

    private string MessageUrl(string messageSid) =>
        $"{MessagingBase}/{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> parameters) =>
        string.Join("&", parameters.Select(p =>
            $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static async Task<SmsSendResult> ReadSendResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = doc.RootElement;

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number
                ? codeProp.GetInt32()
                : (int?)null;
            var errorMessage = root.TryGetProperty("message", out var messageProp) ? messageProp.GetString() : null;
            return new SmsSendResult(false, null, "failed", errorCode, errorMessage ?? $"Provider returned {(int)response.StatusCode}.");
        }

        var sid = root.TryGetProperty("sid", out var sidProp) ? sidProp.GetString() : null;
        var status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
        return new SmsSendResult(true, sid, status, null, null);
    }

    private static SmsMessageInfo ParseMessage(JsonElement element)
    {
        string? GetString(string name) =>
            element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
                ? prop.GetString()
                : null;

        int? GetInt(string name) =>
            element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
                ? prop.GetInt32()
                : (int?)null;

        DateTimeOffset? GetDate(string name) =>
            DateTimeOffset.TryParse(GetString(name), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var value)
                ? value.ToUniversalTime()
                : (DateTimeOffset?)null;

        return new SmsMessageInfo(
            GetString("sid") ?? string.Empty,
            GetString("status"),
            GetInt("error_code"),
            GetString("error_message"),
            GetString("from"),
            GetString("to"),
            GetDate("date_created"),
            GetDate("date_sent"));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            // Deliberately generic: provider error bodies can embed the phone
            // number, which must not end up in logs.
            var _ = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Twilio request failed with status {(int)response.StatusCode}.");
        }
    }
}
