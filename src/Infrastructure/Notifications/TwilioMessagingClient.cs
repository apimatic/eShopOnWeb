using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Talks to Twilio's Messages API (2010-04-01) strictly through its published contract: send,
/// schedule, cancel, fetch, redact and list. All calls are HTTP Basic authenticated and go to the
/// configured messaging base URL (the <c>Twilio:BaseUrl</c> override when set, else api.twilio.com).
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
        _baseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? TwilioSettings.DefaultMessagingBaseUrl
            : _settings.BaseUrl!).TrimEnd('/');
    }

    public string SendingNumber => _settings.FromNumber;

    private string MessagesCollectionUrl => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstanceUrl(string sid) =>
        $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsMessageState> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesCollectionUrl, form, "send", cancellationToken);
    }

    public async Task<SmsMessageState> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduled messages must go through a Messaging Service (per the contract).
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesCollectionUrl, form, "schedule", cancellationToken);
    }

    public async Task<SmsMessageState> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageInstanceUrl(providerMessageSid), form, "cancel", cancellationToken);
    }

    public async Task<SmsMessageState> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating the body to an empty string redacts the text at the provider while the record and
        // its delivery outcome survive.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return await PostMessageAsync(MessageInstanceUrl(providerMessageSid), form, "redact", cancellationToken);
    }

    public async Task<SmsMessageState?> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageInstanceUrl(providerMessageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await ReadAndValidateAsync(response, "fetch", cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for this number's messages over the window. The From filter and the
        // DateSent bounds are applied server-side. The provider's DateSent filter is day-granular and
        // treats a bare date as the start of that day, so we pad the bounds by a day on each side to be
        // sure no message in the window is excluded, then clamp to the exact [from, to] window locally.
        var fromDay = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Keys "DateSent>" and "DateSent<" are the provider's inclusive lower/upper bound filters.
        var query =
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDay)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toDay)}" +
            "&PageSize=1000";

        var results = new List<SmsMessageState>();
        string? nextUrl = MessagesCollectionUrl + query;

        while (nextUrl is not null)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var payload = await ReadAndValidateAsync(response, "list", cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(message));
                }
            }

            nextUrl = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? _baseUrl + next.GetString()
                : null;
        }

        var clamped = new List<SmsMessageState>();
        foreach (var message in results)
        {
            if (message.DateSent.HasValue && message.DateSent.Value >= from && message.DateSent.Value <= to)
            {
                clamped.Add(message);
            }
        }

        return clamped;
    }

    private async Task<SmsMessageState> PostMessageAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var payload = await ReadAndValidateAsync(response, operation, cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    private static async Task<string> ReadAndValidateAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        int? providerCode = null;
        var providerMessage = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                providerCode = codeEl.GetInt32();
            }
            if (doc.RootElement.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                providerMessage = PhoneNumberScrubber.Scrub(msgEl.GetString());
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with just the status.
        }

        var detail = string.IsNullOrEmpty(providerMessage) ? string.Empty : $" - {providerMessage}";
        throw new TwilioApiException(
            $"Twilio {operation} failed (HTTP {(int)response.StatusCode}, code {providerCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}){detail}",
            (int)response.StatusCode,
            providerCode);
    }

    private static SmsMessageState ParseMessage(JsonElement element)
    {
        return new SmsMessageState(
            Sid: GetString(element, "sid") ?? string.Empty,
            Status: GetString(element, "status"),
            To: GetString(element, "to"),
            From: GetString(element, "from"),
            ErrorCode: GetInt(element, "error_code"),
            ErrorMessage: PhoneNumberScrubberOrNull(GetString(element, "error_message")),
            DateSent: GetDate(element, "date_sent"));
    }

    private static string? PhoneNumberScrubberOrNull(string? text) =>
        string.IsNullOrEmpty(text) ? null : PhoneNumberScrubber.Scrub(text);

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
