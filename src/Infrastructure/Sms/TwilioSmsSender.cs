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
using Microsoft.eShopWeb.ApplicationCore.Sms;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsSender"/> over plain HTTP (no SDK), using the
/// Programmable Messaging REST API for sending/reading/reconciling and the Lookup v2 API for
/// number validation. Messaging calls honour <see cref="TwilioSettings.MessagingBaseUrl"/>; the
/// Lookup call always goes to Twilio's lookups host, which that override does not govern.
///
/// Nothing here logs the auth token, a shopper's number, or a message body.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(HttpClient http, TwilioSettings settings, IAppLogger<TwilioSmsSender> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _http.SendAsync(BuildRequest(HttpMethod.Get, url), cancellationToken);

        // Twilio returns 404 (and sometimes 400) for numbers it cannot parse — an unusable destination.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneLookupResult(false, null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsProviderException($"Number lookup failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl)
                    && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var phoneEl) && phoneEl.ValueKind == JsonValueKind.String
            ? phoneEl.GetString()
            : null;

        return valid && !string.IsNullOrEmpty(canonical)
            ? new PhoneLookupResult(true, canonical)
            : new PhoneLookupResult(false, null);
    }

    public async Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Send with an explicit From (so every message is attributable to the configured sending
        // number for reconciliation) together with the Messaging Service.
        var fields = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["Body"] = body,
            ["From"] = _settings.FromNumber
        };
        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        var result = await CreateMessageAsync(fields, cancellationToken);

        // If the account rejects an explicit From alongside the Messaging Service (From not in the
        // service's sender pool), fall back to sending with just the From number.
        if (!result.Success && fields.ContainsKey("MessagingServiceSid"))
        {
            var fromOnly = new Dictionary<string, string>
            {
                ["To"] = toNumber,
                ["Body"] = body,
                ["From"] = _settings.FromNumber
            };
            result = await CreateMessageAsync(fromOnly, cancellationToken);
        }

        if (!result.Success || result.Sid is null)
        {
            throw new SmsProviderException($"Message send failed: {result.ErrorSummary()}");
        }

        return new SmsDispatchResult(result.Sid, result.Status ?? "queued");
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new SmsProviderException("Scheduling a message requires a configured Twilio Messaging Service.");
        }

        var fields = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        var result = await CreateMessageAsync(fields, cancellationToken);

        // Fall back to scheduling without the explicit From if the pool rejects it (the Messaging
        // Service is mandatory for scheduling and is retained).
        if (!result.Success)
        {
            fields.Remove("From");
            result = await CreateMessageAsync(fields, cancellationToken);
        }

        if (!result.Success || result.Sid is null)
        {
            throw new SmsProviderException($"Message scheduling failed: {result.ErrorSummary()}");
        }

        return new SmsDispatchResult(result.Sid, result.Status ?? "scheduled");
    }

    public async Task<SmsDeliveryState> GetDeliveryStateAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var response = await _http.SendAsync(BuildRequest(HttpMethod.Get, MessageUrl(providerMessageId)), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsProviderException($"Reading message state failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(body);
        var parsed = ParseMessage(doc.RootElement);
        return new SmsDeliveryState(parsed.Status ?? "unknown", parsed.ErrorCode, parsed.ErrorMessage);
    }

    public async Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _http.SendAsync(BuildRequest(HttpMethod.Post, MessageUrl(providerMessageId), content), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new SmsProviderException($"Cancelling scheduled message failed: {SummariseError(body, (int)response.StatusCode)}");
        }
    }

    public async Task RedactAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Redaction: overwrite the message body with an empty string at the provider so the text is
        // no longer retrievable, while the record that a message was sent and its status survive.
        var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.SendAsync(BuildRequest(HttpMethod.Post, MessageUrl(providerMessageId), content), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new SmsProviderException($"Disposing message content failed: {SummariseError(body, (int)response.StatusCode)}");
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Ask the provider for this application's own sending number's messages (From filter applied
        // server-side, not by trimming a wider answer). DateSent is date-granular at the provider, so
        // widen the day bounds and trim to the exact [from, to] window client-side to cover the range.
        var fromDay = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&PageSize=1000");
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(fromDay);
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(toDay);

        var nextUrl = MessagesUrl() + query;
        var records = new List<ProviderMessageRecord>();
        var safetyPageLimit = 200;

        while (!string.IsNullOrEmpty(nextUrl) && safetyPageLimit-- > 0)
        {
            using var response = await _http.SendAsync(BuildRequest(HttpMethod.Get, nextUrl!), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SmsProviderException($"Listing provider messages failed with status {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var parsed = ParseMessage(message);
                    if (parsed.Sid is null)
                    {
                        continue;
                    }

                    // Trim to the exact requested window (keep entries not yet sent as well).
                    if (parsed.DateSent is { } sent && (sent < from || sent > to))
                    {
                        continue;
                    }

                    records.Add(new ProviderMessageRecord(parsed.Sid, parsed.To, parsed.From, parsed.Status ?? "unknown", parsed.ErrorCode, parsed.DateSent));
                }
            }

            nextUrl = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? _settings.MessagingBaseUrl + next.GetString()
                : null;
        }

        _logger.LogInformation("Reconciliation pulled {Count} provider message(s) for the configured sending number.", records.Count);
        return records;
    }

    private async Task<TwilioMessageResponse> CreateMessageAsync(IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _http.SendAsync(BuildRequest(HttpMethod.Post, MessagesUrl(), content), cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonElement root = default;
        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(body);
            root = doc.RootElement;
        }
        catch (JsonException)
        {
            // Non-JSON body; fall through with an empty response.
        }

        try
        {
            var parsed = doc is null ? default(ParsedMessage) : ParseMessage(root);
            var errorText = doc is not null && root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String
                ? msgEl.GetString()
                : null;
            var errorCode = doc is not null && root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32()
                : parsed.ErrorCode;

            return new TwilioMessageResponse(
                response.IsSuccessStatusCode && parsed.Sid is not null,
                (int)response.StatusCode,
                parsed.Sid,
                parsed.Status,
                errorCode,
                errorText);
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url);
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        if (content is not null)
        {
            request.Content = content;
        }
        return request;
    }

    private string MessagesUrl() => $"{_settings.MessagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{_settings.MessagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new SmsProviderException("Twilio is not configured (missing Account SID or Auth Token).");
        }
    }

    private static string SummariseError(string body, int statusCode)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
            {
                var code = doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : (int?)null;
                return code is null ? msg.GetString()! : $"{msg.GetString()} (code {code})";
            }
        }
        catch (JsonException)
        {
            // ignore
        }
        return $"status {statusCode}";
    }

    private static ParsedMessage ParseMessage(JsonElement element)
    {
        string? GetString(string name) => element.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

        int? errorCode = null;
        if (element.TryGetProperty("error_code", out var errEl))
        {
            if (errEl.ValueKind == JsonValueKind.Number)
            {
                errorCode = errEl.GetInt32();
            }
            else if (errEl.ValueKind == JsonValueKind.String && int.TryParse(errEl.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedCode))
            {
                errorCode = parsedCode;
            }
        }

        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString("date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) && DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
        {
            dateSent = parsedDate;
        }

        return new ParsedMessage(
            GetString("sid"),
            GetString("to"),
            GetString("from"),
            GetString("status"),
            errorCode,
            GetString("error_message"),
            dateSent);
    }

    private readonly record struct ParsedMessage(
        string? Sid,
        string? To,
        string? From,
        string? Status,
        int? ErrorCode,
        string? ErrorMessage,
        DateTimeOffset? DateSent);

    private readonly record struct TwilioMessageResponse(
        bool Success,
        int HttpStatus,
        string? Sid,
        string? Status,
        int? ErrorCode,
        string? ErrorMessage)
    {
        public string ErrorSummary()
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                return ErrorCode is null ? ErrorMessage! : $"{ErrorMessage} (code {ErrorCode})";
            }
            return $"status {HttpStatus}";
        }
    }
}
