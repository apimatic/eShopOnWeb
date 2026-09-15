using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// Talks to Twilio's Programmable Messaging REST API over plain HTTP. Plain HTTP (rather than the SDK)
/// is used so that <see cref="TwilioSettings.BaseUrl"/> can be honoured verbatim as the base address
/// for every messaging call. Nothing here logs a destination number, a message body, or the auth token.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider
{
    private const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingProvider> _logger;
    private readonly string _baseUrl;

    public TwilioMessagingProvider(HttpClient http, IOptions<TwilioSettings> settings, IAppLogger<TwilioMessagingProvider> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
        _baseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!).TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<ProviderSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber, // send from the configured number so reconciliation can find it
            ["Body"] = body
        };
        using var doc = await PostFormAsync(MessagesUrl(), form, "send", cancellationToken);
        return ReadSendResult(doc.RootElement);
    }

    public async Task<ProviderSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed; the sender is drawn from the
        // service's sender pool. SendAt must be 15 minutes to 35 days out (enforced by the caller).
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        using var doc = await PostFormAsync(MessagesUrl(), form, "schedule", cancellationToken);
        return ReadSendResult(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        // A freshly scheduled message is briefly not yet cancelable and returns 404 (code 20404) while
        // the provider propagates it. Retry across that window so a follow-up is reliably called off —
        // the safety guarantee (it must never go out) cannot hinge on a transient propagation delay.
        const int maxAttempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var _ = await PostFormAsync(MessageUrl(providerMessageId), form, "cancel", cancellationToken);
                return;
            }
            catch (TwilioApiException ex) when (ex.StatusCode == 404 && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    public async Task<ProviderMessageState> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(MessageUrl(providerMessageId), cancellationToken);
        using var doc = await ReadJsonOrThrow(response, "fetch", cancellationToken);
        var root = doc.RootElement;
        return new ProviderMessageState(
            GetString(root, "status"),
            GetInt(root, "error_code"),
            GetString(root, "error_message"));
    }

    public async Task RedactContentAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message text at the provider while keeping the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var _ = await PostFormAsync(MessageUrl(providerMessageId), form, "redact", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for messages from THIS application's configured sending number,
        // within the range. Date filters use the REST inequality keys "DateSent>" / "DateSent<".
        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(Uri.EscapeDataString(IsoUtc(from)));
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(Uri.EscapeDataString(IsoUtc(to)));
        query.Append("&PageSize=1000");

        var next = MessagesUrl() + query;
        var results = new List<ProviderMessageRecord>();

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await _http.GetAsync(next, cancellationToken);
            using var doc = await ReadJsonOrThrow(response, "list", cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var sid = GetString(message, "sid");
                    if (string.IsNullOrEmpty(sid))
                    {
                        continue;
                    }
                    var dateSent = GetDate(message, "date_sent") ?? GetDate(message, "date_created");
                    // Narrow to the exact window (the provider filters by date; we hold to date-time bounds).
                    if (dateSent.HasValue && (dateSent.Value < from || dateSent.Value > to))
                    {
                        continue;
                    }
                    results.Add(new ProviderMessageRecord(
                        sid!, GetString(message, "status"), GetInt(message, "error_code"), dateSent, GetString(message, "to")));
                }
            }

            next = ResolveNextPage(root);
        }

        _logger.LogInformation("Reconciliation listed {0} provider message(s) from the configured sending number.", results.Count);
        return results;
    }

    private string? ResolveNextPage(JsonElement root)
    {
        var nextUri = GetString(root, "next_page_uri");
        if (string.IsNullOrEmpty(nextUri))
        {
            return null;
        }
        // next_page_uri is a path relative to the messaging host; anchor it on the configured base.
        return nextUri!.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? nextUri : _baseUrl + nextUri;
    }

    private async Task<JsonDocument> PostFormAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _http.PostAsync(url, content, cancellationToken);
        return await ReadJsonOrThrow(response, operation, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonOrThrow(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // Extract only Twilio's numeric code; never surface the raw message (it can echo the number).
            int? twilioCode = null;
            try
            {
                using var errorDoc = JsonDocument.Parse(payload);
                twilioCode = GetInt(errorDoc.RootElement, "code");
            }
            catch (JsonException)
            {
                // non-JSON error body — ignore, status code alone is enough
            }
            throw new TwilioApiException((int)response.StatusCode, twilioCode, operation);
        }
        return JsonDocument.Parse(payload);
    }

    private static ProviderSendResult ReadSendResult(JsonElement root)
    {
        var sid = GetString(root, "sid")
            ?? throw new InvalidOperationException("Twilio response did not contain a message sid.");
        return new ProviderSendResult(sid, GetString(root, "status"), GetInt(root, "error_code"));
    }

    private string MessagesUrl() => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static string IsoUtc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
