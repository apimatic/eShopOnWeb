using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's REST API over plain HTTP. The messaging API (send/read/reconcile) honours the
/// optional configured base-url override; the Lookup API always uses its own host. No message body, no
/// destination number and no auth token is ever logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageResourceUrl(string sid) => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private AuthenticationHeaderValue BasicAuth() =>
        new("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));

    private HttpRequestMessage NewRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Authorization = BasicAuth() } };
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    // -------------------- Lookup (own host; not affected by BaseUrl) --------------------

    public async Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var request = NewRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Twilio returns 404 for a number it cannot parse into a valid form.
            var reason = TryReadErrorMessage(payload) ?? "The number could not be validated.";
            return new PhoneLookupResult(false, null, reason);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var isValid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var phoneEl) && phoneEl.ValueKind == JsonValueKind.String
            ? phoneEl.GetString()
            : null;

        if (!isValid || string.IsNullOrEmpty(canonical))
            return new PhoneLookupResult(false, null, "The number is not a valid SMS destination.");

        return new PhoneLookupResult(true, canonical, null);
    }

    // -------------------- Send / schedule --------------------

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body),
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduled messages must be sent through a Messaging Service, not a bare From number.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, MessagesCollectionUrl, form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ReadError(payload);
            _logger.LogWarning("Twilio rejected a message create: HTTP {0}, code {1}.", (int)response.StatusCode, code ?? "?");
            return new SmsSendResult(false, null, MessageDeliveryStatus.NotSent, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        var msg = ParseMessage(doc.RootElement);
        return new SmsSendResult(true, msg.Sid, msg.Status, msg.ErrorCode, null);
    }

    // -------------------- Cancel scheduled --------------------

    public async Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", MessageDeliveryStatus.Canceled) };
        using var request = NewRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ReadError(payload);
            return new SmsSendResult(false, providerMessageSid, MessageDeliveryStatus.Scheduled, code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        var msg = ParseMessage(doc.RootElement);
        return new SmsSendResult(true, msg.Sid, msg.Status, msg.ErrorCode, null);
    }

    // -------------------- Fetch --------------------

    public async Task<ProviderMessage?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, MessageResourceUrl(providerMessageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    // -------------------- Redact body --------------------

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Overwriting the body with an empty string redacts the content at Twilio while keeping the record.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var request = NewRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, message) = ReadError(payload);
            throw new HttpRequestException($"Twilio body redaction failed (code {code}): {message}");
        }
    }

    // -------------------- List for reconciliation --------------------

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for our own sender's messages across the range (inclusive, day-granular filter).
        // The '>' and '<' operators are appended to the parameter name and must be percent-encoded.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={fromDate}" +
                    $"&DateSent%3C={toDate}" +
                    $"&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUrl = MessagesCollectionUrl + query;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = NewRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ReadError(payload);
                throw new HttpRequestException($"Twilio message listing failed (code {code}): {message}");
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                    results.Add(ParseMessage(m));
            }

            // Follow next_page_uri (a relative path) against the configured messaging host until exhausted.
            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var next = nextEl.GetString();
                if (!string.IsNullOrEmpty(next))
                    nextUrl = MessagingBase + next;
            }
        }

        return results;
    }

    // -------------------- parsing helpers --------------------

    private static ProviderMessage ParseMessage(JsonElement el)
    {
        string sid = GetString(el, "sid") ?? string.Empty;
        string status = GetString(el, "status") ?? string.Empty;
        string? to = GetString(el, "to");
        string? fromNumber = GetString(el, "from");
        string? errorCode = GetErrorCode(el);
        DateTimeOffset? dateSent = ParseTwilioDate(GetString(el, "date_sent"));
        DateTimeOffset? dateCreated = ParseTwilioDate(GetString(el, "date_created"));
        return new ProviderMessage(sid, status, to, fromNumber, errorCode, dateSent, dateCreated);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetErrorCode(JsonElement el)
    {
        if (!el.TryGetProperty("error_code", out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetInt32().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => v.GetString(),
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static (string? code, string? message) ReadError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            string? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetInt32().ToString(CultureInfo.InvariantCulture)
                : null;
            string? message = GetString(root, "message");
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? TryReadErrorMessage(string payload) => ReadError(payload).message;
}
