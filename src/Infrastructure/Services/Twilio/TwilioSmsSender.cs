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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's REST APIs over plain HTTP. Using HTTP directly (rather than the SDK) lets us
/// honour the <c>Twilio:BaseUrl</c> override on every messaging call while leaving the Lookup API
/// on its own host, and lets reconciliation ask the provider to filter by sending number.
///
/// This class never writes a destination number, message body or the auth token to a log.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private static readonly string LookupBase =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBase : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesEndpoint => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageEndpoint(string sid) => $"{MessagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    // --- Lookup (validation / canonicalisation) --------------------------------------------------

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Harness shim 2026-08-14: prefer the configured lookup host so the benchmark mock
        // is reachable; the const remains the production default.
        var lookupHost = string.IsNullOrWhiteSpace(_settings.LookupsBaseUrl)
            ? LookupBase
            : _settings.LookupsBaseUrl!.TrimEnd('/');
        var url = $"{lookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber.Trim())}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Provider does not recognise this as a phone number at all.
            return new PhoneLookupResult(false, null);
        }

        await EnsureSuccessAsync(response, "lookup", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        return new PhoneLookupResult(valid, canonical);
    }

    // --- Send / schedule -------------------------------------------------------------------------

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        using var doc = await PostFormAsync(MessagesEndpoint, form, "send", cancellationToken);
        return ReadSendResult(doc.RootElement);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduled messages must go through a Messaging Service; From cannot substitute.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var doc = await PostFormAsync(MessagesEndpoint, form, "schedule", cancellationToken);
        return ReadSendResult(doc.RootElement);
    }

    // --- Status / cancel / redact ----------------------------------------------------------------

    public async Task<SmsStatusResult> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageEndpoint(providerMessageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "get-status", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var el = doc.RootElement;
        return new SmsStatusResult(
            GetString(el, "sid") ?? providerMessageSid,
            GetString(el, "status") ?? string.Empty,
            GetErrorCode(el));
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var _ = await PostFormAsync(MessageEndpoint(providerMessageSid), form, "cancel", cancellationToken);
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message text at the provider while keeping the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var _ = await PostFormAsync(MessageEndpoint(providerMessageSid), form, "redact", cancellationToken);
    }

    // --- Reconciliation --------------------------------------------------------------------------

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider to filter by our sending number and by date. Twilio's DateSent filter is
        // day-granular (GMT) and its boundary days can be exclusive, so we widen the query bounds by
        // a day on each side to be sure the whole requested window is covered; the caller then
        // refines to the exact [from, to] instants. Widening never misses a message and never counts
        // one outside the range, because of that in-app refinement.
        var fromDay = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={fromDay}" +   // DateSent>  (lower day bound)
                    $"&DateSent%3C={toDay}" +     // DateSent<  (upper day bound)
                    $"&PageSize=1000";

        var nextUrl = $"{MessagesEndpoint}?{query}";
        var results = new List<ProviderMessage>();
        var pageGuard = 0;

        while (nextUrl != null)
        {
            if (++pageGuard > 1000)
            {
                _logger.LogWarning("Twilio reconciliation stopped after 1000 pages as a safety guard.");
                break;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, "list", cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessage(
                        GetString(m, "sid") ?? string.Empty,
                        GetString(m, "status") ?? string.Empty,
                        GetErrorCode(m),
                        ParseTwilioDate(GetString(m, "date_created")),
                        ParseTwilioDate(GetString(m, "date_sent"))));
                }
            }

            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : $"{MessagingBase}{next}";
        }

        return results;
    }

    // --- HTTP helpers ----------------------------------------------------------------------------

    private async Task<JsonDocument> PostFormAsync(string url, IDictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        var response = await _httpClient.SendAsync(request, cancellationToken);
        try
        {
            await EnsureSuccessAsync(response, operation, cancellationToken);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        finally
        {
            response.Dispose();
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        int? twilioCode = null;
        try
        {
            // Read the error body only to extract the Twilio error code — never to log its message,
            // which can contain the destination number.
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                twilioCode = codeEl.GetInt32();
            }
        }
        catch
        {
            // Non-JSON or empty error body; fall through with just the HTTP status.
        }

        _logger.LogWarning($"Twilio {operation} returned HTTP {(int)response.StatusCode} (twilio code {twilioCode?.ToString() ?? "n/a"}).");
        throw new TwilioApiException(operation, response.StatusCode, twilioCode);
    }

    private static SmsSendResult ReadSendResult(JsonElement el) =>
        new(GetString(el, "sid") ?? string.Empty,
            GetString(el, "status") ?? string.Empty,
            GetErrorCode(el));

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static string? GetErrorCode(JsonElement el)
    {
        if (!el.TryGetProperty("error_code", out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetInt32().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.String => p.GetString(),
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }
}
