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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/> over the verified REST contract.
///
/// Hosts: the Messaging API (send / read / cancel / redact / reconcile) lives on api.twilio.com and
/// is the only host governed by <c>Twilio:BaseUrl</c>. The Lookup API lives on lookups.twilio.com and
/// is deliberately NOT affected by that override.
///
/// Sensitive data discipline: destination numbers and message bodies are never written to logs.
/// The auth token is only ever placed in the Authorization header, never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    // HARNESS SHIM, added 2026-08-17 AFTER the agent finished - NOT this run's work.
    // Number lookup rides a SECOND Twilio host (lookups.twilio.com) which Twilio:BaseUrl does not
    // govern, so without this the call leaves for the real host and ~18 checks per run die in setup.
    // The env var is supplied by the grading profile (app.config.Twilio__LookupsBaseUrl -> Proc.cs:236).
    // Default is unchanged, so the build behaves exactly as delivered when the var is absent.
    private static readonly string LookupBase =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } __lookupsOverride
            ? __lookupsOverride
            : "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBase;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBase
            : _settings.BaseUrl.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string SendingNumber => _settings.FromNumber;

    private string MessagesCollectionUrl => $"{_messagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageResourceUrl(string sid) => $"{_messagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 is served from lookups.twilio.com and is not governed by Twilio:BaseUrl.
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Twilio returns 404 (code 20404) when the number cannot be parsed at all.
            return new PhoneLookupResult(false, null, "Number could not be recognised.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryParseError(payload);
            throw new SmsProviderException($"Lookup failed ({(int)response.StatusCode}): {message}", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
        {
            var reason = "Number is not a valid, reachable destination.";
            if (root.TryGetProperty("validation_errors", out var ve) && ve.ValueKind == JsonValueKind.Array && ve.GetArrayLength() > 0)
            {
                reason = $"Number is not valid ({ve[0].GetString()}).";
            }
            return new PhoneLookupResult(false, null, reason);
        }

        return new PhoneLookupResult(true, canonical, null);
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // Send from the application's own configured number so reconciliation can find it by From.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        var json = await PostMessageAsync(form, cancellationToken);
        return ReadSendResult(json);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service. We additionally pin From to our sending number so
        // the eventual message reconciles under Twilio:FromNumber. If the account's messaging service
        // does not have that number in its sender pool, fall back to letting the service pick a sender
        // (the message is still queued with the provider, which is what matters).
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["ScheduleType"] = "fixed"
        };

        try
        {
            var json = await PostMessageAsync(form, cancellationToken);
            return ReadSendResult(json);
        }
        catch (SmsProviderException)
        {
            form.Remove("From");
            var json = await PostMessageAsync(form, cancellationToken);
            return ReadSendResult(json);
        }
    }

    public async Task<ProviderMessageStatus> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageResourceUrl(messageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryParseError(payload);
            throw new SmsProviderException($"Cancel failed ({(int)response.StatusCode}): {message}", code);
        }
        return ReadStatus(payload);
    }

    public async Task<ProviderMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageResourceUrl(messageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryParseError(payload);
            throw new SmsProviderException($"Fetch failed ({(int)response.StatusCode}): {message}", code);
        }
        return ReadStatus(payload);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redaction is a Message update with an empty Body: the text is cleared on Twilio's side while
        // the record (sid, status, timestamps) survives.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessageResourceUrl(messageSid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryParseError(payload);
            throw new SmsProviderException($"Redact failed ({(int)response.StatusCode}): {message}", code);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask Twilio for this application's own sending number's messages directly (server-side From
        // filter), so other traffic on the account is never returned. DateSent inequality filters bound
        // the range. The key names contain > and < which must be percent-encoded (%3E / %3C).
        var fromEnc = Uri.EscapeDataString(_settings.FromNumber);
        var afterEnc = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var beforeEnc = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        var nextUrl = $"{MessagesCollectionUrl}?From={fromEnc}&DateSent%3E={afterEnc}&DateSent%3C={beforeEnc}&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        var safety = 0;
        while (nextUrl != null && safety++ < 1000)
        {
            using var response = await _http.GetAsync(nextUrl, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = TryParseError(payload);
                throw new SmsProviderException($"List failed ({(int)response.StatusCode}): {message}", code);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(ReadMessageRecord(m));
                }
            }

            nextUrl = root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? _messagingBase + next.GetString()
                : null;
        }

        return results;
    }

    private async Task<string> PostMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(MessagesCollectionUrl, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryParseError(payload);
            // Note: no To/Body is included in the exception — only the provider's own code/message.
            throw new SmsProviderException($"Send failed ({(int)response.StatusCode}): {message}", code);
        }
        return payload;
    }

    private static SmsSendResult ReadSendResult(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = root.GetProperty("sid").GetString()!;
        var status = GetStringOrNull(root, "status");
        var errorCode = GetIntOrNull(root, "error_code");
        return new SmsSendResult(sid, status, errorCode);
    }

    private static ProviderMessageStatus ReadStatus(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new ProviderMessageStatus(GetStringOrNull(root, "status"), GetIntOrNull(root, "error_code"));
    }

    private static ProviderMessageRecord ReadMessageRecord(JsonElement m)
    {
        var sid = GetStringOrNull(m, "sid") ?? string.Empty;
        var status = GetStringOrNull(m, "status");
        var fromNum = GetStringOrNull(m, "from");
        var toNum = GetStringOrNull(m, "to");
        var errorCode = GetIntOrNull(m, "error_code");
        DateTimeOffset? dateSent = null;
        var raw = GetStringOrNull(m, "date_sent");
        if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }
        return new ProviderMessageRecord(sid, status, fromNum, toNum, dateSent, errorCode);
    }

    private static (int? code, string message) TryParseError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var code = GetIntOrNull(root, "code");
            var message = GetStringOrNull(root, "message") ?? "Unknown provider error.";
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, "Unparseable provider error response.");
        }
    }

    private static string? GetStringOrNull(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetIntOrNull(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null
        };
    }
}
