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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to the provider's classic messaging API (send, schedule, fetch, cancel, redact, list) over
/// HTTP with Basic auth, form-encoded bodies and snake_case responses. Honours the optional
/// <see cref="TwilioSettings.BaseUrl"/> override for every messaging call.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    // Redacts anything that looks like a phone number from provider-supplied text before it can
    // reach an exception message or a log, so a shopper's number is never written to logs.
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\-\s().]{6,}\d", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBase;
    private readonly string _messagesPath;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;

        _messagingBase = (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!)
            .TrimEnd('/');
        _messagesPath = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync($"{_messagingBase}{_messagesPath}", form, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduling a message requires a Messaging Service and a fixed send time (ISO 8601, UTC).
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync($"{_messagingBase}{_messagesPath}", form, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response.StatusCode, payload);
        return ParseMessage(payload);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Cancel a not-yet-sent scheduled message by moving it to the canceled status.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        await PostMessageAsync(
            $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json",
            form, cancellationToken);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redact the body text at the provider by updating it to an empty string. The record survives.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(
            $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json",
            form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // The provider's DateSent filter is day-granular UTC; widen by a day on each side and then
        // filter precisely in memory so the exact range is honoured and the whole range is covered.
        var fromDay = fromUtc.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = toUtc.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Ask the provider only for this application's own sending number's messages.
        var nextUrl =
            $"{_messagingBase}{_messagesPath}?PageSize=1000" +
            $"&From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={fromDay}&DateSent%3C={toDay}";

        var results = new List<ProviderMessage>();
        var safetyPageLimit = 100;

        while (nextUrl is not null && safetyPageLimit-- > 0)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var parsed = ParseMessage(message);
                    if (parsed.DateSentUtc is null || (parsed.DateSentUtc >= fromUtc && parsed.DateSentUtc <= toUtc))
                    {
                        results.Add(parsed);
                    }
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var relative = next.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    nextUrl = $"{_messagingBase}{relative}";
                }
            }
        }

        return results;
    }

    // ---- HTTP helpers ---------------------------------------------------------------------------

    private async Task<ProviderMessage> PostMessageAsync(string url, IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(url, content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new MessagingProviderException($"Could not reach the messaging provider: {Scrub(ex.Message)}", innerException: ex);
        }

        using (response)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response.StatusCode, payload);
            return ParseMessage(payload);
        }
    }

    private static void EnsureSuccess(HttpStatusCode statusCode, string payload)
    {
        if ((int)statusCode is >= 200 and < 300)
        {
            return;
        }

        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
            }

            if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                message = msgEl.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through with a generic message.
        }

        var detail = string.IsNullOrEmpty(message) ? $"HTTP {(int)statusCode}" : Scrub(message!);
        throw new MessagingProviderException($"Messaging provider request failed ({(int)statusCode}): {detail}", code);
    }

    private static ProviderMessage ParseMessage(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement el)
    {
        var sid = GetString(el, "sid") ?? string.Empty;
        var status = GetString(el, "status") ?? string.Empty;
        var to = GetString(el, "to");
        var from = GetString(el, "from");
        var errorMessage = GetString(el, "error_message");
        var body = GetString(el, "body");

        int? errorCode = null;
        if (el.TryGetProperty("error_code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
        {
            errorCode = codeEl.GetInt32();
        }

        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(el, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) && TryParseProviderDate(dateSentRaw!, out var parsed))
        {
            dateSent = parsed;
        }

        // error_message can, in principle, echo the destination; scrub before it leaves the client.
        return new ProviderMessage(sid, status, to, from, errorCode, Scrub(errorMessage), dateSent, body);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool TryParseProviderDate(string raw, out DateTimeOffset value)
    {
        // Classic API returns RFC 2822 (e.g. "Thu, 24 Aug 2023 05:01:45 +0000").
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value))
        {
            return true;
        }

        string[] formats =
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss K",
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'"
        };
        return DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value);
    }

    private static string? Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? text : PhoneLike.Replace(text, "[redacted-number]");
}
