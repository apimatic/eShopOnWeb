using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Talks to the Twilio messaging API (the 2010-04-01 Messages resource) over HTTP exactly as the provider
/// documents it: form-encoded requests, HTTP Basic auth, JSON responses. Every call targets the configured
/// messaging base address, which may be overridden by <c>Twilio:BaseUrl</c>.
///
/// Immediate sends are addressed from the configured <c>FromNumber</c> so the reconciliation report can ask
/// the provider for exactly this application's traffic. Scheduled sends go through the Messaging Service (a
/// provider requirement for scheduling).
/// </summary>
public class TwilioMessagingService : ISmsGateway
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;

    public TwilioMessagingService(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;
    }

    private string MessagesPath => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessagePath(string sid) => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<MessageDispatchResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        using var response = await _http.PostAsync(MessagesPath, new FormUrlEncodedContent(form), cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        return ToDispatchResult(root, scheduledAt: null);
    }

    public async Task<MessageDispatchResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; the sender is chosen from its pool.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var response = await _http.PostAsync(MessagesPath, new FormUrlEncodedContent(form), cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        return ToDispatchResult(root, scheduledAt: sendAt);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await _http.PostAsync(MessagePath(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
        await ReadAsync(response, cancellationToken);
    }

    public async Task<MessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await GetWithRetryAsync(MessagePath(providerMessageSid), cancellationToken);
        var root = await ReadAsync(response, cancellationToken);
        return new MessageState(
            GetString(root, "sid") ?? providerMessageSid,
            GetString(root, "status"),
            GetInt(root, "error_code"),
            Redacted(GetString(root, "error_message")));
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Per the provider, POSTing an empty Body redacts the message text while leaving the record intact.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await _http.PostAsync(MessagePath(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
        await ReadAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromIso = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Ask the provider for exactly this application's sending number over the range — the From filter is
        // applied by the provider, not after the fact. DateSent> / DateSent< are the documented range filters.
        var nextPath =
            $"{MessagesPath}?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toIso)}" +
            "&PageSize=1000";

        var results = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(nextPath))
        {
            using var response = await GetWithRetryAsync(nextPath!, cancellationToken);
            var root = await ReadAsync(response, cancellationToken);

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessage(
                        GetString(m, "sid"),
                        GetString(m, "status"),
                        GetString(m, "to"),
                        GetString(m, "from"),
                        ParseRfc2822(GetString(m, "date_sent")),
                        GetInt(m, "error_code"),
                        Redacted(GetString(m, "error_message"))));
                }
            }

            nextPath = GetString(root, "next_page_uri");
        }

        return results;
    }

    private MessageDispatchResult ToDispatchResult(JsonElement root, DateTimeOffset? scheduledAt)
    {
        var sid = GetString(root, "sid")
            ?? throw new TwilioApiException("Twilio accepted the request but returned no message SID.");
        return new MessageDispatchResult(
            sid,
            GetString(root, "status"),
            GetInt(root, "error_code"),
            Redacted(GetString(root, "error_message")),
            scheduledAt);
    }

    /// <summary>GET with a small bounded retry for transient (429 / 5xx / transport) failures.</summary>
    private async Task<HttpResponseMessage> GetWithRetryAsync(string requestUri, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var response = await _http.GetAsync(requestUri, cancellationToken);
                if (attempt < maxAttempts && IsTransient(response.StatusCode))
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode code) =>
        code == HttpStatusCode.TooManyRequests || (int)code >= 500;

    /// <summary>Ensures success and returns the parsed JSON root; on failure throws a number-free exception.</summary>
    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            string? code = null, message = null;
            try
            {
                using var errDoc = JsonDocument.Parse(content);
                code = GetString(errDoc.RootElement, "code");
                message = GetString(errDoc.RootElement, "message");
            }
            catch (JsonException) { /* non-JSON error body */ }

            var detail = message is not null ? $": {PhoneRedactor.Redact(message)}" : string.Empty;
            var codePart = code is not null ? $" (code {code})" : string.Empty;
            throw new TwilioApiException($"Twilio messaging API returned {(int)response.StatusCode}{codePart}{detail}");
        }

        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.Clone();
    }

    private static string? Redacted(string? text) => text is null ? null : PhoneRedactor.Redact(text);

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind is not JsonValueKind.Null
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString())
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        !string.IsNullOrEmpty(value) &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
}
