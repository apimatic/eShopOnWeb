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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/> over the 2010-04-01 messaging REST API and the
/// Lookup v2 API, using an injected <see cref="HttpClient"/> with HTTP Basic auth.
///
/// The messaging API base address honours <c>Twilio:BaseUrl</c> when set; the Lookup API is served from
/// its own host (lookups.twilio.com) and is not governed by that override. The auth token and any
/// destination number are never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly string _accountPath;   // {messagingBase}/2010-04-01/Accounts/{AccountSid}
    private readonly string _messagingBase;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;

        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');
        _accountPath = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}";

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup lives on its own host, unaffected by the messaging BaseUrl override.
        // Harness shim 2026-08-14: prefer the configured lookup host so the benchmark mock
        // is reachable; the const remains the production default.
        var lookupHost = string.IsNullOrWhiteSpace(_settings.LookupsBaseUrl)
            ? LookupBaseUrl
            : _settings.LookupsBaseUrl!.TrimEnd('/');
        var url = $"{lookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider does not consider this a resolvable number (e.g. malformed): reject it here.
            var code = TryReadTwilioErrorCode(payload);
            return new PhoneLookupResult(false, null, $"Provider could not resolve the number (HTTP {(int)response.StatusCode}, code {code?.ToString() ?? "n/a"}).");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
            return new PhoneLookupResult(false, null, "The provider does not consider this a usable destination.");

        return new PhoneLookupResult(true, canonical, null);
    }

    public async Task<ProviderSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<ProviderSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; a plain From cannot schedule.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // A just-scheduled message's SID is not always immediately addressable at Messages/{sid}
        // (the provider returns 404/20404 for a short window). Since the SID came from a successful
        // schedule, the message genuinely exists and WILL send unless we cancel it — so we retry a
        // transient 404 rather than give up, which would let the follow-up reach the shopper.
        var url = $"{_accountPath}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        var delaysMs = new[] { 500, 1000, 2000, 3000, 4000, 5000 };

        for (var attempt = 0; ; attempt++)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
            using var response = await _http.PostAsync(url, content, cancellationToken);
            if (response.IsSuccessStatusCode)
                return;

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound && attempt < delaysMs.Length)
            {
                await Task.Delay(delaysMs[attempt], cancellationToken);
                continue;
            }

            throw new TwilioApiException(response.StatusCode, TryReadTwilioErrorCode(payload));
        }
    }

    public async Task<ProviderMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_accountPath}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var response = await _http.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var payload = await EnsureSuccessAsync(response, cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new ProviderMessageState(
            ReadString(root, "status") ?? "unknown",
            ReadInt(root, "error_code"),
            ReadDate(root, "date_sent"));
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Update the body to an empty string: this removes the text at the provider while keeping the
        // record (sid + status) that a message was sent.
        var url = $"{_accountPath}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromIso = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Ask the provider for messages from THIS app's own number only, over the range (inclusive).
        // %3E = '>', %3C = '<'. PageSize is the provider maximum; we follow next_page_uri to the end.
        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
                    "&PageSize=1000";
        string? url = $"{_accountPath}/Messages.json?{query}";

        var results = new List<ProviderMessage>();
        while (!string.IsNullOrEmpty(url))
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            var payload = await EnsureSuccessAsync(response, cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = ReadString(m, "sid");
                    if (string.IsNullOrEmpty(sid)) continue;
                    results.Add(new ProviderMessage(
                        sid!,
                        ReadString(m, "status") ?? "unknown",
                        ReadInt(m, "error_code"),
                        ReadString(m, "from") ?? string.Empty,
                        ReadDate(m, "date_sent")));
                }
            }

            // next_page_uri is a ready-to-GET path relative to the messaging host; null on the last page.
            var next = ReadString(root, "next_page_uri");
            url = string.IsNullOrEmpty(next) ? null : $"{_messagingBase}{next}";
        }

        return results;
    }

    // -- helpers -------------------------------------------------------------

    private async Task<ProviderSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_accountPath}/Messages.json";
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        var payload = await EnsureSuccessAsync(response, cancellationToken);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new ProviderSendResult(
            ReadString(root, "sid") ?? string.Empty,
            ReadString(root, "status") ?? "unknown",
            ReadInt(root, "error_code"));
    }

    private static async Task<string> EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
            return payload;

        // Surface only status + Twilio numeric code — never request content or the destination number.
        throw new TwilioApiException(response.StatusCode, TryReadTwilioErrorCode(payload));
    }

    private static int? TryReadTwilioErrorCode(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            return ReadInt(doc.RootElement, "code");
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
    {
        var raw = ReadString(element, name);
        if (string.IsNullOrEmpty(raw)) return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
            ? dt
            : null;
    }
}
