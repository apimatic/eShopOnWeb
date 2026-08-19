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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to the SMS provider over its REST API. Messaging operations (send / read / redact /
/// list) go to the messaging base address, which <see cref="TwilioSettings.BaseUrl"/> may
/// override; number validation goes to the provider's separate Lookup host, which the override
/// does not govern. Phone numbers and the auth secret are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private const string LookupBase = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBase;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> options, ILogger<TwilioSmsProvider> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;
        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBase
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    // ---- Lookup (validation & canonicalisation) -------------------------------------------

    public async Task<PhoneValidationResult> ValidateAndCanonicalizeAsync(
        string rawPhoneNumber, string? defaultCountryCode = null, CancellationToken cancellationToken = default)
    {
        // Pass the raw input through unmodified; supply CountryCode for national-format input.
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber)}";
        if (!string.IsNullOrWhiteSpace(defaultCountryCode))
            url += $"?CountryCode={Uri.EscapeDataString(defaultCountryCode)}";

        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A client-side status (e.g. malformed number) is a rejection, not an outage.
            if ((int)response.StatusCode is >= 400 and < 500)
            {
                _logger.LogInformation("Number validation rejected by provider (HTTP {Status}).", (int)response.StatusCode);
                return PhoneValidationResult.Invalid(new[] { "The number was not accepted by the provider." });
            }

            throw ToApiException(response.StatusCode, payload, "validate number");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        if (valid && !string.IsNullOrEmpty(canonical))
            return PhoneValidationResult.Valid(canonical!);

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
            errors.AddRange(errEl.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!));
        if (errors.Count == 0)
            errors.Add("The number is not a valid, assignable destination.");

        return PhoneValidationResult.Invalid(errors);
    }

    // ---- Send / schedule / cancel / read / redact -----------------------------------------

    public async Task<SentSms> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        var json = await PostFormAsync(MessagesUrl(), form, "send message", cancellationToken);
        return ParseMessage(json);
    }

    public async Task<SentSms> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; the sender is drawn from its pool.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        var json = await PostFormAsync(MessagesUrl(), form, "schedule message", cancellationToken);
        return ParseMessage(json);
    }

    public async Task<SentSms> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var json = await PostFormAsync(MessageUrl(providerMessageSid), form, "cancel scheduled message", cancellationToken);
        return ParseMessage(json);
    }

    public async Task<SentSms?> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
            throw ToApiException(response.StatusCode, payload, "fetch message");

        return ParseMessage(payload);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the text content at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostFormAsync(MessageUrl(providerMessageSid), form, "redact message body", cancellationToken);
    }

    // ---- List for reconciliation ----------------------------------------------------------

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sender's messages (server-side filter),
        // bounded by date so the whole range is covered. Precise datetime filtering is applied by
        // the caller. The inequality operators are part of the parameter names (DateSent> / DateSent<).
        // The provider's DateSent bounds are day-granular and the upper bound is exclusive of the
        // named day's start, so we push it to the day AFTER `to` to include all of `to`'s own day.
        var fromDate = fromInclusive.ToUniversalTime().Date
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDateExclusive = toInclusive.ToUniversalTime().Date.AddDays(1)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}"
                    + $"&{Uri.EscapeDataString("DateSent>")}={fromDate}"
                    + $"&{Uri.EscapeDataString("DateSent<")}={toDateExclusive}"
                    + "&PageSize=1000";

        var nextUrl = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}";
        var results = new List<ProviderMessage>();
        var safetyPageLimit = 100;

        while (nextUrl != null && safetyPageLimit-- > 0)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ToApiException(response.StatusCode, payload, "list messages");

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessage(
                        Sid: GetString(m, "sid") ?? string.Empty,
                        To: GetString(m, "to"),
                        From: GetString(m, "from"),
                        Status: GetString(m, "status"),
                        ErrorCode: GetInt(m, "error_code"),
                        DateSent: GetDate(m, "date_sent")));
                }
            }

            var relativeNext = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrWhiteSpace(relativeNext)
                ? null
                : $"{_messagingBase}{relativeNext}";
        }

        return results;
    }

    // ---- helpers --------------------------------------------------------------------------

    private string MessagesUrl() =>
        $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<string> PostFormAsync(
        string url, IDictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ToApiException(response.StatusCode, payload, operation);
        return payload;
    }

    private static SentSms ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        return new SentSms(
            Sid: GetString(m, "sid") ?? string.Empty,
            Status: GetString(m, "status"),
            ErrorCode: GetInt(m, "error_code"),
            ErrorMessage: GetString(m, "error_message"),
            DateSent: GetDate(m, "date_sent"));
    }

    private TwilioApiException ToApiException(HttpStatusCode status, string payload, string operation)
    {
        int? code = null;
        string message = $"Provider call failed while trying to {operation} (HTTP {(int)status}).";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            code = GetInt(root, "code");
            var providerMessage = GetString(root, "message");
            if (!string.IsNullOrWhiteSpace(providerMessage))
                message = $"Provider rejected the request to {operation}: {providerMessage} (HTTP {(int)status}, code {code}).";
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message. Never echo the raw body (privacy).
        }

        _logger.LogWarning("Messaging provider call to {Operation} failed with HTTP {Status} (code {Code}).",
            operation, (int)status, code);
        return new TwilioApiException(status, code, message);
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
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement el, string name)
    {
        var raw = GetString(el, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}
