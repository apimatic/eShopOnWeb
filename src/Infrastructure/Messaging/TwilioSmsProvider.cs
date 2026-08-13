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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Talks to the messaging provider over its documented REST API. Every method here maps onto one
/// documented operation:
/// <list type="bullet">
///   <item>Lookup — <c>GET {lookup}/v2/PhoneNumbers/{PhoneNumber}</c></item>
///   <item>Send / Schedule — <c>POST {messaging}/2010-04-01/Accounts/{AccountSid}/Messages.json</c></item>
///   <item>Fetch — <c>GET  {messaging}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json</c></item>
///   <item>Cancel / Redact — <c>POST {messaging}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json</c></item>
///   <item>List / reconcile — <c>GET {messaging}/2010-04-01/Accounts/{AccountSid}/Messages.json</c></item>
/// </list>
/// Authentication is HTTP Basic with the Account SID as username and the auth token as password.
/// The auth token and shopper phone numbers are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _settings = settings.Value;
        _httpClient = httpClient;

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    private string MessagesBase => $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages";

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{_settings.LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}?Fields=validation";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // The provider answers 404 for a number it cannot parse or place — an unusable destination,
        // not a transport failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_FOUND" });
        }

        var payload = await ReadAsync(response, "lookup", cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
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

        return new PhoneNumberLookupResult(valid, canonical, errors);
    }

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, "send", cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service capability: ScheduleType=fixed + SendAt, with a
        // MessagingServiceSid. The From is pinned to the configured sender (a member of the pool) so the
        // message, if it ever goes out, is attributable to that number.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(form, "schedule", cancellationToken);
    }

    private async Task<ProviderMessage> CreateMessageAsync(Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync($"{MessagesBase}.json", content, cancellationToken);
        var payload = await ReadAsync(response, operation, cancellationToken);
        return ParseMessage(payload);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"{MessagesBase}/{Uri.EscapeDataString(messageSid)}.json", cancellationToken);
        var payload = await ReadAsync(response, "fetch", cancellationToken);
        return ParseMessage(payload);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _httpClient.PostAsync($"{MessagesBase}/{Uri.EscapeDataString(messageSid)}.json", content, cancellationToken);
        var payload = await ReadAsync(response, "cancel", cancellationToken);
        return ParseMessage(payload);
    }

    public async Task RedactAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider while leaving the resource in place.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _httpClient.PostAsync($"{MessagesBase}/{Uri.EscapeDataString(messageSid)}.json", content, cancellationToken);
        await ReadAsync(response, "redact", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for messages from this application's configured sending number, so
        // other traffic on the account is never counted. The date filter is applied at day granularity
        // by the provider and refined to the exact window in-process.
        var fromDate = from.ToUniversalTime().Date;
        var toDateInclusive = to.ToUniversalTime().Date.AddDays(1); // widen so the whole 'to' day is covered

        var query =
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={fromDate:yyyy-MM-dd}" +   // DateSent> : on and after
            $"&DateSent%3C={toDateInclusive:yyyy-MM-dd}" + // DateSent< : before
            "&PageSize=1000";

        var results = new List<ProviderMessage>();
        var nextUrl = $"{MessagesBase}.json{query}";

        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var payload = await ReadAsync(response, "list", cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var msg = ParseMessage(m);
                    // Refine to the exact requested window; keep undated messages out of a "sent" report.
                    if (msg.DateSent is { } sent && sent >= from && sent <= to)
                    {
                        results.Add(msg);
                    }
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var nextPath = next.GetString();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    // next_page_uri is a path relative to the messaging host.
                    nextUrl = $"{_settings.MessagingBaseUrl}{nextPath}";
                }
            }
        }

        return results;
    }

    // ---- helpers -------------------------------------------------------------------------------

    private static async Task<string> ReadAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return payload;
        }

        // Surface the provider's own error code, but never the response body verbatim: for send errors it
        // can echo the destination number, which must not reach logs.
        int? code = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.TryGetInt32(out var c))
            {
                code = c;
            }
        }
        catch (JsonException)
        {
            // non-JSON error body; ignore
        }

        throw new SmsProviderException(
            $"Messaging provider returned HTTP {(int)response.StatusCode} for '{operation}'" +
            (code.HasValue ? $" (provider error code {code.Value})." : "."))
        {
            ProviderErrorCode = code
        };
    }

    private static ProviderMessage ParseMessage(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement root)
    {
        return new ProviderMessage(
            Sid: GetString(root, "sid") ?? string.Empty,
            Status: GetString(root, "status"),
            To: GetString(root, "to"),
            From: GetString(root, "from"),
            DateSent: GetDate(root, "date_sent"),
            ErrorCode: GetInt(root, "error_code"),
            ErrorMessage: GetString(root, "error_message"));
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
        {
            return n;
        }
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s))
        {
            return s;
        }
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement root, string name)
    {
        var raw = GetString(root, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        // The provider returns RFC 2822 timestamps, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }
}
