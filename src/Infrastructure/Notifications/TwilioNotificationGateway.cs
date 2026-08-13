using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio implementation of <see cref="IOrderNotificationGateway"/>, built directly against the
/// Twilio OpenAPI contract in <c>api-specs/twilio</c>:
/// <list type="bullet">
///   <item>Lookups v2 <c>GET /v2/PhoneNumbers/{PhoneNumber}</c> for destination validation.</item>
///   <item>API 2010-04-01 Messages resource for send / schedule / cancel / fetch / redact / list.</item>
/// </list>
/// Auth is HTTP Basic (<c>AccountSid:AuthToken</c>). The messaging API base address honours the
/// optional <c>Twilio:BaseUrl</c> override; Lookups always uses its own host. The auth token and
/// destination numbers are never written to logs.
/// </summary>
public class TwilioNotificationGateway : IOrderNotificationGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 100;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioNotificationGateway> _logger;
    private readonly string _messagingBaseUrl;
    private readonly AuthenticationHeaderValue _authHeader;

    public TwilioNotificationGateway(
        HttpClient httpClient,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioNotificationGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _authHeader = new AuthenticationHeaderValue("Basic", basic);
    }

    // ---------------------------------------------------------------- Lookups

    public async Task<PhoneValidationResult> ValidateDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await SendAsync(HttpMethod.Get, url, form: null, "validate destination", cancellationToken).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

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

        return new PhoneValidationResult(valid, canonical, errors);
    }

    // ---------------------------------------------------------------- Send / schedule

    public async Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, "send message", cancellationToken).ConfigureAwait(false);
    }

    public async Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new NotificationGatewayException("A messaging service SID is required to schedule a message but none is configured.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return await CreateMessageAsync(form, "schedule message", cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProviderMessage> CreateMessageAsync(Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var response = await SendAsync(HttpMethod.Post, url, form, operation, cancellationToken).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var message = MapMessage(doc.RootElement);
        _logger.LogInformation("Twilio {Operation} accepted: sid={Sid} status={Status}", operation, message.Sid, message.Status);
        return message;
    }

    // ---------------------------------------------------------------- Cancel / fetch / redact

    public async Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{providerMessageSid}.json";
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await SendAsync(HttpMethod.Post, url, form, "cancel scheduled message", cancellationToken).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var message = MapMessage(doc.RootElement);
        _logger.LogInformation("Twilio cancel accepted: sid={Sid} status={Status}", message.Sid, message.Status);
        return message;
    }

    public async Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{providerMessageSid}.json";
        using var response = await SendAsync(HttpMethod.Get, url, form: null, "fetch message", cancellationToken).ConfigureAwait(false);
        using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        return MapMessage(doc.RootElement);
    }

    public async Task DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Twilio redaction: POST the message with an empty Body removes the text content at the provider,
        // leaving the message record (and its status) intact.
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{providerMessageSid}.json";
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await SendAsync(HttpMethod.Post, url, form, "dispose message content", cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Twilio content disposal accepted: sid={Sid}", providerMessageSid);
    }

    // ---------------------------------------------------------------- Reconciliation list

    public async Task<IReadOnlyList<ProviderMessage>> ListSentByConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for messages sent from THIS application's own configured sending number
        // within the range, rather than filtering a wider answer after the fact. The DateSent
        // inequality filters accept a full ISO-8601 GMT timestamp, which we use so the whole range is
        // covered precisely (a day-only bound resolves to 00:00:00 and would drop same-day messages).
        var fromStamp = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toStamp = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(Uri.EscapeDataString(fromStamp));
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(Uri.EscapeDataString(toStamp));
        query.Append("&PageSize=1000");

        var nextUrl = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}";

        var results = new List<ProviderMessage>();
        var pages = 0;
        while (nextUrl is not null)
        {
            if (++pages > MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation stopped after {Pages} pages; results may be incomplete.", MaxReconciliationPages);
                break;
            }

            using var response = await SendAsync(HttpMethod.Get, nextUrl, form: null, "list messages", cancellationToken).ConfigureAwait(false);
            using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messagesEl.EnumerateArray())
                {
                    var message = MapMessage(m);
                    var effective = message.DateSent ?? message.DateCreated;
                    if (effective is null || (effective >= from && effective <= to))
                    {
                        results.Add(message);
                    }
                }
            }

            nextUrl = root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String
                ? $"{_messagingBaseUrl}{nextEl.GetString()}"
                : null;
        }

        _logger.LogInformation("Reconciliation retrieved {Count} provider message(s) from the configured sender across {Pages} page(s).", results.Count, pages);
        return results;
    }

    // ---------------------------------------------------------------- HTTP plumbing

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, IReadOnlyDictionary<string, string>? form, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Headers = { Authorization = _authHeader } };
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new NotificationGatewayException($"Could not reach the messaging provider to {operation}.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var detail = await ReadProviderErrorAsync(response, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            response.Dispose();
            // Log status/code only — never the destination number or token.
            _logger.LogWarning("Twilio {Operation} returned HTTP {Status} (code {Code}).", operation, status, detail.Code);
            throw new NotificationGatewayException($"Messaging provider rejected the request to {operation} (HTTP {status}, code {detail.Code}).");
        }

        return response;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(int? Code, string? Message)> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return (null, null);
            }
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            int? code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null;
            string? msg = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
            return (code, msg);
        }
        catch
        {
            return (null, null);
        }
    }

    private static ProviderMessage MapMessage(JsonElement m)
    {
        string sid = GetString(m, "sid") ?? string.Empty;
        string? status = GetString(m, "status");
        int? errorCode = m.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number ? ec.GetInt32() : null;
        string? errorMessage = GetString(m, "error_message");
        string? to = GetString(m, "to");
        string? fromNo = GetString(m, "from");
        string? body = GetString(m, "body");
        var dateSent = ParseTwilioDate(GetString(m, "date_sent"));
        var dateCreated = ParseTwilioDate(GetString(m, "date_created"));
        return new ProviderMessage(sid, status, errorCode, errorMessage, to, fromNo, dateSent, dateCreated, body);
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // Twilio returns RFC 2822 dates, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
