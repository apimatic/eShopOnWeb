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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's messaging API (the classic <c>/2010-04-01</c> Message resource) over HTTP.
/// This is the only place that knows the wire format; everything above it uses <see cref="ISmsGateway"/>.
///
/// The base address is the <c>Twilio:BaseUrl</c> override when set, otherwise Twilio's default
/// messaging host. Requests are HTTP Basic authenticated (Account SID / Auth Token) and form-encoded.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!.TrimEnd('/');

        // HTTP Basic auth: Account SID as username, Auth Token as password. The token is never logged.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string FromNumber => _options.FromNumber;

    public async Task<SmsMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };

        var json = await PostFormAsync(MessagesCollectionUrl(), form, toNumber, cancellationToken);
        var message = ParseMessage(json);
        _logger.LogInformation("Sent message {Sid} (status {Status}).", message.Sid, message.Status);
        return message;
    }

    public async Task<SmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed; the provider holds and sends it.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        var json = await PostFormAsync(MessagesCollectionUrl(), form, toNumber, cancellationToken);
        var message = ParseMessage(json);
        _logger.LogInformation("Scheduled message {Sid} (status {Status}) for {SendAt:o}.", message.Sid, message.Status, sendAt);
        return message;
    }

    public async Task<SmsMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageResourceUrl(messageSid), cancellationToken);
        var json = await ReadAndEnsureSuccessAsync(response, null, cancellationToken);
        return ParseMessage(json);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Status=canceled is the only value this parameter accepts; it stops a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        await PostFormAsync(MessageResourceUrl(messageSid), form, null, cancellationToken);
        _logger.LogInformation("Cancelled scheduled message {Sid}.", messageSid);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider while the record and outcome survive.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostFormAsync(MessageResourceUrl(messageSid), form, null, cancellationToken);
        _logger.LogInformation("Redacted content of message {Sid} at the provider.", messageSid);
    }

    public async Task<IReadOnlyList<SmsMessage>> ListMessagesFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this number's messages (the From filter), not a wider answer filtered later.
        // DateSent filtering is date-granular, so query a window widened by a day and trim precisely below.
        var fromDay = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"?From={Uri.EscapeDataString(_options.FromNumber)}"
                    + $"&DateSent%3E={fromDay}"   // DateSent> (on/after)
                    + $"&DateSent%3C={toDay}"     // DateSent< (on/before)
                    + "&PageSize=1000";
        var nextUrl = MessagesCollectionUrl() + query;

        var results = new List<SmsMessage>();
        var pageGuard = 0;
        while (!string.IsNullOrEmpty(nextUrl) && pageGuard++ < 200)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var json = await ReadAndEnsureSuccessAsync(response, null, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in messages.EnumerateArray())
                {
                    var message = ParseMessage(element);
                    var effective = message.DateSent ?? GetDate(element, "date_created");
                    // Trim the widened window back to the requested range; keep undated (freshly queued) messages.
                    if (effective is null || (effective >= from && effective <= to))
                        results.Add(message);
                }
            }

            nextUrl = ResolveNextPage(root);
        }

        _logger.LogInformation("Reconciliation listed {Count} provider message(s) from the configured number.", results.Count);
        return results;
    }

    // ----- URLs ----------------------------------------------------------------------------------

    private string MessagesCollectionUrl() =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageResourceUrl(string messageSid) =>
        $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private string? ResolveNextPage(JsonElement root)
    {
        if (!root.TryGetProperty("next_page_uri", out var next) || next.ValueKind != JsonValueKind.String)
            return null;
        var relative = next.GetString();
        if (string.IsNullOrEmpty(relative))
            return null;
        // next_page_uri is relative to the messaging host; resolve against the (possibly overridden) base.
        return $"{_messagingBaseUrl}{relative}";
    }

    // ----- HTTP + parsing ------------------------------------------------------------------------

    private async Task<string> PostFormAsync(string url, IDictionary<string, string> form, string? destinationToRedact, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        return await ReadAndEnsureSuccessAsync(response, destinationToRedact, cancellationToken);
    }

    private static async Task<string> ReadAndEnsureSuccessAsync(HttpResponseMessage response, string? destinationToRedact, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
            return payload;

        // Surface the provider's own error code/message, but never the destination number.
        int? providerCode = null;
        string providerMessage = $"HTTP {(int)response.StatusCode}";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number)
                providerCode = code.GetInt32();
            if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                providerMessage = message.GetString() ?? providerMessage;
        }
        catch (JsonException)
        {
            // non-JSON error body; keep the HTTP status summary
        }

        if (!string.IsNullOrEmpty(destinationToRedact))
            providerMessage = providerMessage.Replace(destinationToRedact, "<redacted>", StringComparison.Ordinal);

        throw new TwilioApiException((int)response.StatusCode, providerCode,
            $"Twilio messaging API error (HTTP {(int)response.StatusCode}, code {providerCode?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}): {providerMessage}");
    }

    private static SmsMessage ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseMessage(doc.RootElement);
    }

    private static SmsMessage ParseMessage(JsonElement element)
    {
        var sid = GetString(element, "sid") ?? string.Empty;
        var status = GetString(element, "status") ?? string.Empty;
        int? errorCode = null;
        if (element.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number)
            errorCode = ec.GetInt32();
        var errorMessage = GetString(element, "error_message");
        var from = GetString(element, "from");
        var to = GetString(element, "to");
        var dateSent = GetDate(element, "date_sent");
        return new SmsMessage(sid, status, errorCode, errorMessage, from, to, dateSent);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrEmpty(raw))
            return null;
        // Twilio classic returns RFC 2822 timestamps, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        return null;
    }
}
