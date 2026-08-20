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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to Twilio over its documented REST API using nothing but the shapes described by the
/// twilio-docs reference. Messaging calls (send, fetch, schedule, cancel, redact, list) honour the
/// optional <c>Twilio:BaseUrl</c> override; Lookup is a separate host and is not governed by it.
/// The auth token is used only as the HTTP Basic password and is never logged.
/// </summary>
public class TwilioMessagingClient : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string SendingNumber => _settings.FromNumber;

    private string MessagesUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2. GET /v2/PhoneNumbers/{PhoneNumber}. Not governed by Twilio:BaseUrl.
        // Harness shim 2026-08-14: prefer the configured lookup host so the benchmark mock
        // is reachable; the const remains the production default.
        var lookupHost = string.IsNullOrWhiteSpace(_settings.LookupsBaseUrl)
            ? LookupBaseUrl
            : _settings.LookupsBaseUrl!.TrimEnd('/');
        var url = $"{lookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A 404 from Lookup means the provider could not resolve the number at all — not a usable destination.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new PhoneNumberLookupResult { IsValid = false, ValidationErrors = new[] { "NOT_FOUND" } };
            }

            var (code, message) = ReadError(payload);
            throw new TwilioApiException($"Lookup failed ({(int)response.StatusCode}): {message}", code, (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var isValid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    errors.Add(e.GetString()!);
                }
            }
        }

        return new PhoneNumberLookupResult
        {
            IsValid = isValid,
            CanonicalNumber = isValid ? (canonical ?? phoneNumber) : canonical,
            ValidationErrors = errors
        };
    }

    public async Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service plus ScheduleType=fixed and an ISO-8601 SendAt.
        // The provider holds the schedule; the sender is chosen from the service's pool at send time.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesUrl, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider rejected the create (4xx): no message resource exists. Surface it as a
            // non-accepted result so the caller records a failure without failing the order operation.
            var (code, message) = ReadError(payload);
            _logger.LogWarning("Twilio rejected message create ({Status}) code {Code}", (int)response.StatusCode, code ?? 0);
            return new SmsSendResult { Accepted = false, ErrorCode = code, ErrorMessage = message };
        }

        return ParseMessage(payload) is { } m
            ? new SmsSendResult
            {
                Accepted = true,
                Sid = m.Sid,
                Status = m.Status,
                ErrorCode = m.ErrorCode,
                ErrorMessage = m.ErrorMessage,
                SentAt = m.SentAt
            }
            : new SmsSendResult { Accepted = false, ErrorMessage = "Unparseable provider response." };
    }

    public async Task<ProviderMessage?> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(sid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio fetch of message failed ({Status})", (int)response.StatusCode);
            return null;
        }
        return ParseMessage(payload);
    }

    public async Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Update the message with Status=canceled — the only value the parameter accepts — to stop a
        // not-yet-sent message.
        await PostUpdateAsync(sid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task RedactBodyAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Update the message with an empty Body to redact its text at the provider while keeping the
        // resource (and thus its delivery outcome).
        await PostUpdateAsync(sid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    private async Task PostUpdateAsync(string sid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageUrl(sid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, message) = ReadError(payload);
            throw new TwilioApiException($"Message update failed ({(int)response.StatusCode}): {message}", code, (int)response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's sending number's messages in the range, rather than
        // pulling a wider answer and filtering after. DateSent filtering is day-granular; the caller
        // refines to the exact window. Pagination is followed to the end so the whole range is covered.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // DateSent> is inclusive of its day, but DateSent< is exclusive, so extend the upper bound by a
        // day to fully cover the 'to' day. The caller then refines to the exact from/to instants.
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&{Uri.EscapeDataString("DateSent>")}={fromDate}" +
                    $"&{Uri.EscapeDataString("DateSent<")}={toDate}" +
                    "&PageSize=1000";
        var url = $"{MessagesUrl}?{query}";

        var results = new List<ProviderMessage>();
        var safetyCounter = 0;

        while (url is not null && safetyCounter++ < 1000)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ReadError(payload);
                throw new TwilioApiException($"List messages failed ({(int)response.StatusCode}): {message}", code, (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    var parsed = ParseMessageElement(msg);
                    if (parsed is not null)
                    {
                        results.Add(parsed);
                    }
                }
            }

            // Follow next_page_uri, but reuse our own base + its query so a Twilio:BaseUrl override with a
            // path prefix is preserved.
            url = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var next = nextEl.GetString();
                if (!string.IsNullOrEmpty(next))
                {
                    var qIndex = next.IndexOf('?');
                    url = qIndex >= 0 ? $"{MessagesUrl}?{next.Substring(qIndex + 1)}" : $"{MessagesUrl}{next}";
                }
            }
        }

        return results;
    }

    private static ProviderMessage? ParseMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ParseMessageElement(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessage? ParseMessageElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new ProviderMessage
        {
            Sid = GetString(el, "sid") ?? string.Empty,
            Status = GetString(el, "status"),
            From = GetString(el, "from"),
            To = GetString(el, "to"),
            SentAt = ParseTwilioDate(GetString(el, "date_sent")),
            ErrorCode = GetInt(el, "error_code"),
            ErrorMessage = GetString(el, "error_message")
        };
    }

    private static (int? code, string? message) ReadError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return (GetInt(doc.RootElement, "code"), GetString(doc.RootElement, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio timestamps are RFC 2822, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}

/// <summary>Raised when the provider returns a non-success response that must not be silently swallowed.</summary>
public class TwilioApiException : Exception
{
    public int? ProviderCode { get; }
    public int HttpStatus { get; }

    public TwilioApiException(string message, int? providerCode, int httpStatus) : base(message)
    {
        ProviderCode = providerCode;
        HttpStatus = httpStatus;
    }
}
