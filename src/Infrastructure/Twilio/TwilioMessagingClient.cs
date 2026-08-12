using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Talks to the Twilio messaging API over HTTP. The base address, credentials and sending identity
/// are supplied by <see cref="TwilioOptions"/> and configured on the injected <see cref="HttpClient"/>
/// (including the basic-auth header, which keeps the auth token out of this code). Every messaging call
/// goes through the configured messaging base URL.
/// </summary>
public class TwilioMessagingClient : ISmsSender
{
    private const int MaxPages = 100;

    private readonly HttpClient _http;
    private readonly TwilioOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public string SendingNumber => _options.FromNumber;

    private string MessagesPath =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessagePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(MessagesPath, form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; a bare From number cannot be scheduled.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync(MessagesPath, form, cancellationToken);
    }

    public Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostMessageAsync(MessagePath(messageSid), form, cancellationToken);
    }

    public async Task<SmsSendResult> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessagePath(messageSid), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);
        using var doc = JsonDocument.Parse(content);
        return ReadMessage(doc.RootElement);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message content at Twilio while keeping the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagePath(messageSid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toInclusive, CancellationToken cancellationToken = default)
    {
        var fromDate = fromInclusive.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Twilio's date-only DateSent bounds sit at midnight, so DateSent<=<to-date> would exclude
        // everything sent during the "to" day. Widen the upper bound to the next day and let the exact
        // in-app filter below trim back to the requested window.
        var toDateExclusiveUpper = toInclusive.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Ask the provider only for messages from our own sending number (pushed down, not filtered
        // after the fact). DateSent bounds are date-granular; we refine to the exact window below.
        var firstPath = new StringBuilder(MessagesPath)
            .Append("?From=").Append(Uri.EscapeDataString(_options.FromNumber))
            .Append("&PageSize=1000")
            .Append("&DateSent%3E=").Append(fromDate)             // DateSent>=
            .Append("&DateSent%3C=").Append(toDateExclusiveUpper) // DateSent<= (next day; trimmed in-app)
            .ToString();

        var results = new List<ProviderMessageRecord>();
        var nextPath = firstPath;
        var pages = 0;

        while (nextPath is not null && pages < MaxPages)
        {
            pages++;
            using var response = await _http.GetAsync(nextPath, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var record = ReadProviderRecord(m);
                    if (record is null)
                    {
                        continue;
                    }

                    // Refine the date-granular provider filter down to the exact requested window.
                    if (record.DateSent is { } sent && (sent < fromInclusive || sent > toInclusive))
                    {
                        continue;
                    }

                    // Defensive: the provider filter already restricts by From.
                    if (!string.IsNullOrEmpty(record.From) &&
                        !string.Equals(record.From, _options.FromNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(record);
                }
            }

            nextPath = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var value = next.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    // next_page_uri is an absolute path; make it relative so any configured base path is kept.
                    nextPath = value.TrimStart('/');
                }
            }
        }

        return results;
    }

    private async Task<SmsSendResult> PostMessageAsync(string path, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content);
        using var doc = JsonDocument.Parse(content);
        return ReadMessage(doc.RootElement);
    }

    private static SmsSendResult ReadMessage(JsonElement el)
    {
        var sid = GetString(el, "sid");
        var status = GetString(el, "status") ?? string.Empty;
        var errorCode = GetInt(el, "error_code");
        var errorMessage = TwilioText.RedactNumbers(GetString(el, "error_message"));
        return new SmsSendResult(sid, status, errorCode, errorMessage);
    }

    private static ProviderMessageRecord? ReadProviderRecord(JsonElement el)
    {
        var sid = GetString(el, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }
        var status = GetString(el, "status") ?? string.Empty;
        var from = GetString(el, "from");
        var errorCode = GetInt(el, "error_code");

        DateTimeOffset? dateSent = null;
        var raw = GetString(el, "date_sent");
        if (!string.IsNullOrEmpty(raw) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessageRecord(sid, status, from, dateSent, errorCode);
    }

    private static void EnsureSuccess(HttpResponseMessage response, string content)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        string message = $"Twilio request failed with status {(int)response.StatusCode}.";
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            code = GetInt(root, "code");
            var providerMessage = GetString(root, "message");
            if (!string.IsNullOrEmpty(providerMessage))
            {
                message = providerMessage!;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        throw new TwilioApiException(response.StatusCode, code, TwilioText.RedactNumbers(message)!);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => null
        };
    }
}
