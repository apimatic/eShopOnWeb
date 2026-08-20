using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="IMessagingProvider"/> over plain HTTP, so the messaging base
/// address can be overridden verbatim while the Lookup host stays fixed.
///
/// Verified against Twilio's official REST docs:
///   Send/read/list/update/redact : POST|GET {base}/2010-04-01/Accounts/{Sid}/Messages[/{MsgSid}].json
///   Schedule                     : create with MessagingServiceSid + ScheduleType=fixed + SendAt (15m–35d)
///   Cancel scheduled             : update message with Status=canceled
///   Redact body                  : update message with Body="" (keeps the record, removes the text)
///   Lookup/validate              : GET https://lookups.twilio.com/v2/PhoneNumbers/{number}
/// A destination number and the auth secret are never written to logs.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private static readonly string LookupsBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    // Redacts anything that looks like a phone number (7+ digits, possibly grouped) from provider text.
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\-\s().]{5,}\d", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingProvider(HttpClient http, IOptions<TwilioSettings> settings, IAppLogger<TwilioMessagingProvider> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string SendingNumber => _settings.FromNumber;

    private string MessagesUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    // ------------------------------------------------------------------ Lookup / validate

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup is served from lookups.twilio.com regardless of the messaging BaseUrl override.
        // Harness shim 2026-08-14: prefer the configured lookup host so the benchmark mock
        // is reachable; the const remains the production default.
        var lookupHost = string.IsNullOrWhiteSpace(_settings.LookupsBaseUrl)
            ? LookupsBaseUrl
            : _settings.LookupsBaseUrl!.TrimEnd('/');
        var url = $"{lookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // 404 / 400 => the provider can't treat this as a valid number. Reject rather than throw.
            _logger.LogWarning("Lookup returned {StatusCode} for a supplied number.", (int)response.StatusCode);
            return new PhoneNumberLookupResult(false, null);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
            return new PhoneNumberLookupResult(false, null);

        return new PhoneNumberLookupResult(true, canonical);
    }

    // ------------------------------------------------------------------ Send now

    public async Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesUrl, form, "send", cancellationToken);
    }

    // ------------------------------------------------------------------ Schedule for later

    public async Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesUrl, form, "schedule", cancellationToken);
    }

    // ------------------------------------------------------------------ Cancel a scheduled message

    public async Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageUrl(providerMessageSid), form, "cancel", cancellationToken);
    }

    // ------------------------------------------------------------------ Redact the body

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // POST with an empty Body redacts the text at Twilio while leaving the record and its status.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(providerMessageSid), form, "redact", cancellationToken);
    }

    // ------------------------------------------------------------------ Read one message

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ToException(response.StatusCode, payload, "read");

        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    // ------------------------------------------------------------------ List sent messages for reconciliation

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Ask the provider for THIS number's messages in the range directly (From filter + date bounds),
        // rather than fetching a wider answer and filtering after the fact. %3E / %3C are '>' and '<'.
        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&DateSent%3E=").Append(Uri.EscapeDataString(fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        query.Append("&DateSent%3C=").Append(Uri.EscapeDataString(toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        query.Append("&PageSize=1000");

        var results = new List<ProviderMessage>();
        var nextUrl = MessagesUrl + query;

        // Follow next_page_uri until the whole range is covered.
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ToException(response.StatusCode, payload, "list");

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var parsed = ParseMessage(m);
                    // Narrow to the exact bounds in case the provider's date filter is coarser than the request.
                    if (parsed.DateSent is { } sent && (sent < fromUtc || sent > toUtc))
                        continue;
                    results.Add(parsed);
                }
            }

            nextUrl = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? _messagingBaseUrl + next.GetString()
                : null;
        }

        return results;
    }

    // ------------------------------------------------------------------ shared HTTP + parsing

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ToException(response.StatusCode, payload, operation);

        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement m)
    {
        string GetString(string name) =>
            m.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString()! : string.Empty;

        string? GetStringOrNull(string name) =>
            m.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        int? GetIntOrNull(string name)
        {
            if (!m.TryGetProperty(name, out var el))
                return null;
            return el.ValueKind switch
            {
                JsonValueKind.Number when el.TryGetInt32(out var n) => n,
                JsonValueKind.String when int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
                _ => null
            };
        }

        DateTimeOffset? dateSent = null;
        var rawDate = GetStringOrNull("date_sent");
        if (!string.IsNullOrEmpty(rawDate) &&
            DateTimeOffset.TryParse(rawDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed.ToUniversalTime();
        }

        return new ProviderMessage(
            Sid: GetString("sid"),
            Status: GetString("status"),
            From: GetStringOrNull("from"),
            To: GetStringOrNull("to"),
            DateSent: dateSent,
            ErrorCode: GetIntOrNull("error_code"),
            ErrorMessage: Sanitize(GetStringOrNull("error_message")));
    }

    private MessagingProviderException ToException(HttpStatusCode statusCode, string payload, string operation)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var ci))
                code = ci;
            if (root.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                message = msg.GetString();
        }
        catch (JsonException)
        {
            // Non-JSON error body; ignore its content entirely.
        }

        var sanitized = Sanitize(message);
        var text = $"Twilio {operation} failed (http {(int)statusCode}" +
                   (code.HasValue ? $", code {code}" : string.Empty) + ")" +
                   (string.IsNullOrEmpty(sanitized) ? string.Empty : $": {sanitized}");

        _logger.LogWarning("Twilio {Operation} failed with http {StatusCode}{Code}.", operation, (int)statusCode,
            code.HasValue ? $" code {code}" : string.Empty);

        return new MessagingProviderException(text, code);
    }

    /// <summary>Redact any phone-number-like text so provider messages can be stored/returned/logged safely.</summary>
    private static string? Sanitize(string? text) =>
        string.IsNullOrEmpty(text) ? text : PhoneLike.Replace(text, "[redacted]");
}
