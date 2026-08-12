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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

/// <summary>
/// Talks to Twilio over plain HTTP (form-urlencoded, HTTP Basic auth), against the contract
/// confirmed from Twilio's official documentation:
///   - Send/read/reconcile: {messaging base}/2010-04-01/Accounts/{Sid}/Messages[...].json
///     where {messaging base} is Twilio:BaseUrl when set, else https://api.twilio.com
///   - Schedule: same Messages endpoint with MessagingServiceSid + ScheduleType=fixed + SendAt
///   - Cancel:  POST the message with Status=canceled
///   - Redact:  POST the message with an empty Body (clears the text, keeps the record)
///   - Lookup/validate: https://lookups.twilio.com/v2/PhoneNumbers/{number} (a different host,
///     deliberately NOT governed by Twilio:BaseUrl)
/// The auth token is only ever placed in the Authorization header; it is never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const int ListPageSize = 200;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _messagingBaseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!).TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 lives on lookups.twilio.com, not the messaging host.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return new PhoneNumberLookupResult(false, null); // Twilio could not parse/find the number

        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

        return new PhoneNumberLookupResult(valid, canonical);
    }

    public async Task<SentMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber, // immediate sends originate from the configured number
            ["Body"] = body
        };

        using var response = await _httpClient.PostAsync(MessagesUrl(), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        return ReadSentMessage(doc.RootElement);
    }

    public async Task<SentMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service (a bare From is not allowed) plus ScheduleType=fixed and SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var response = await _httpClient.PostAsync(MessagesUrl(), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        return ReadSentMessage(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<MessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        return new MessageState(ReadString(root, "status") ?? "unknown", ReadInt(root, "error_code"));
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Posting an empty Body redacts the message text at Twilio while keeping the record and its status.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), new FormUrlEncodedContent(form), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromNumberAsync(
        string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask Twilio directly for this From number's messages, filtered by send date at the source.
        // DateSent supports day granularity; we widen to whole days here and refine to the exact
        // [from, to] window in-app below so the whole range is covered and nothing outside it counts.
        var fromDay = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"?From={Uri.EscapeDataString(fromNumber)}" +
                    $"&DateSent%3E={fromDay}" +   // DateSent>=
                    $"&DateSent%3C={toDay}" +     // DateSent<=
                    $"&PageSize={ListPageSize}";
        var nextUrl = MessagesUrl() + query;

        var results = new List<ProviderMessage>();
        while (nextUrl is not null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var dateSent = ReadDate(m, "date_sent");
                    // Refine day-granular results to the exact requested window.
                    if (dateSent is not null && (dateSent < from || dateSent > to))
                        continue;

                    results.Add(new ProviderMessage(
                        ReadString(m, "sid") ?? string.Empty,
                        ReadString(m, "status") ?? "unknown",
                        ReadInt(m, "error_code"),
                        ReadString(m, "to") ?? string.Empty,
                        dateSent));
                }
            }

            // Follow Twilio's own pagination cursor. next_page_uri is a host-relative path.
            var next = ReadString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : _messagingBaseUrl + next;
        }

        return results;
    }

    // ----- URL builders -----------------------------------------------------------------------

    private string MessagesUrl() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    // ----- response helpers -------------------------------------------------------------------

    private static SentMessage ReadSentMessage(JsonElement root) =>
        new(ReadString(root, "sid") ?? string.Empty, ReadString(root, "status") ?? "unknown", ReadInt(root, "error_code"));

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Throws a sanitized <see cref="TwilioApiException"/> on failure. The raw response body is not
    /// surfaced because a messaging error can echo the destination number.
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        int? twilioCode = null;
        try
        {
            using var doc = await ReadJsonAsync(response, cancellationToken);
            twilioCode = ReadInt(doc.RootElement, "code");
        }
        catch
        {
            // ignore non-JSON error bodies — never propagate raw content
        }

        throw new TwilioApiException(response.StatusCode, twilioCode);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p))
            return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string name)
    {
        var raw = ReadString(element, name);
        if (string.IsNullOrEmpty(raw))
            return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }
}
