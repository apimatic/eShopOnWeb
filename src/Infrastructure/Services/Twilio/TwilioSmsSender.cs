using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Sends, reads, cancels, redacts and lists messages through the provider's messaging REST API
/// (the classic /2010-04-01 Message resource). The base host honours <see cref="TwilioSettings.BaseUrl"/>
/// when set. Requests are form-encoded; responses are JSON. Nothing here logs a destination number or
/// the auth token.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsSender> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsSender(HttpClient http, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsSender> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
    }

    public string FromNumber => _settings.FromNumber;

    private string MessagesUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service. We also pin From to our own sending number so the
        // scheduled message is attributable to it for reconciliation; if the provider rejects the pin
        // (number not in the service's sender pool) we fall back to the service picking the sender.
        var sendAt = sendAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var withFrom = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt)
        };

        var result = await CreateMessageAsync(withFrom, cancellationToken);
        if (result.Accepted)
            return result;

        _logger.LogWarning("Scheduled send with pinned sender was rejected (code {Code}); retrying via Messaging Service pool.", result.ErrorCode ?? 0);
        var poolOnly = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt)
        };
        return await CreateMessageAsync(poolOnly, cancellationToken);
    }

    public async Task<SmsSendResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(MessageUrl(providerSid), cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, _) = ParseError(json);
                return new SmsSendResult { Accepted = false, ErrorCode = code, FailureReason = $"provider fetch failed (code {code})" };
            }
            return ParseMessage(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Fetching message {Sid} failed: {Error}", providerSid, ex.Message);
            return new SmsSendResult { Accepted = false, FailureReason = "network error" };
        }
    }

    public async Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // POST to the message resource with Status=canceled — the only value that parameter accepts.
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageUrl(providerSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = ParseError(json);
            throw new InvalidOperationException($"Provider refused to cancel message {providerSid} (code {code}).");
        }
    }

    public async Task RedactContentAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // Redact the body by updating it to an empty string; the message record and its status survive.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageUrl(providerSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = ParseError(json);
            throw new InvalidOperationException($"Provider refused to redact message {providerSid} (code {code}).");
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's sending number's messages over the range. The From
        // filter is applied at the provider, not after the fact, so other traffic on the account is
        // excluded. The DateSent filter is day-granular and compares against each day's midnight, so we
        // floor the lower bound to its day and take the upper bound as the day AFTER 'to'; that
        // guarantees the whole range is covered. We then trim to the exact [from, to] window below.
        var fromUtcNormalised = fromUtc.ToUniversalTime();
        var toUtcNormalised = toUtc.ToUniversalTime();
        var fromDate = fromUtcNormalised.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toUtcNormalised.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={fromDate}" +   // DateSent>=fromDate (00:00 UTC of that day)
                    $"&DateSent%3C={toDate}" +     // DateSent<=toDate (00:00 UTC of the day after 'to')
                    $"&PageSize=1000";
        var nextUrl = MessagesUrl + query;

        var records = new List<ProviderMessageRecord>();
        var safetyPages = 0;
        while (nextUrl is not null && safetyPages++ < 1000)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, _) = ParseError(json);
                throw new InvalidOperationException($"Provider list messages failed (code {code}).");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                    records.Add(ReadRecord(m));
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var rel = next.GetString();
                if (!string.IsNullOrEmpty(rel))
                    nextUrl = _messagingBaseUrl + rel; // classic next_page_uri is relative to the messaging host
            }
        }

        // Trim the day-granular result back to the exact requested window. Records without a parseable
        // send time were not actually sent in-range and are excluded.
        return records
            .Where(r => r.DateSent.HasValue && r.DateSent.Value >= fromUtcNormalised && r.DateSent.Value <= toUtcNormalised)
            .ToList();
    }

    private async Task<SmsSendResult> CreateMessageAsync(List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(MessagesUrl, content, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, _) = ParseError(json);
                // FailureReason is deliberately code-only: the provider's message text can echo the
                // destination number, which must never reach logs.
                return new SmsSendResult
                {
                    Accepted = false,
                    ErrorCode = code,
                    Status = NotificationStatus.NotSent,
                    FailureReason = $"provider rejected (code {code})"
                };
            }
            return ParseMessage(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Create message failed before acceptance: {Error}", ex.Message);
            return new SmsSendResult { Accepted = false, Status = NotificationStatus.NotSent, FailureReason = "network error" };
        }
    }

    private static SmsSendResult ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new SmsSendResult
        {
            Accepted = true,
            ProviderSid = GetString(root, "sid"),
            Status = GetString(root, "status"),
            ErrorCode = GetInt(root, "error_code")
        };
    }

    private static ProviderMessageRecord ReadRecord(JsonElement m)
    {
        DateTimeOffset? dateSent = null;
        var raw = GetString(m, "date_sent");
        if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            dateSent = parsed;

        return new ProviderMessageRecord
        {
            Sid = GetString(m, "sid") ?? string.Empty,
            Status = GetString(m, "status"),
            To = GetString(m, "to"),
            From = GetString(m, "from"),
            DateSent = dateSent,
            ErrorCode = GetInt(m, "error_code")
        };
    }

    private static (int? code, string? message) ParseError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return (GetInt(root, "code"), GetString(root, "message"));
        }
        catch
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
            return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), out var i) => i,
            _ => null
        };
    }
}
