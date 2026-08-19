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

namespace Microsoft.eShopWeb.Infrastructure.Notifications.Twilio;

/// <summary>
/// A hand-written Twilio client built strictly to the OpenAPI specs in <c>api-specs/twilio</c>:
/// the Messaging API (<c>twilio_api_v2010</c>) for sending, reading, cancelling, redacting and
/// listing messages, and the Lookup API (<c>twilio_lookups_v2</c>) for validating destinations.
/// Auth is HTTP Basic (Account SID + Auth Token) per the specs' <c>accountSid_authToken</c> scheme.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 200;

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _authHeader;
    private readonly string _messagingBaseUrl;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));

        // Twilio:BaseUrl, when set, overrides the base address of the messaging API only.
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!.TrimEnd('/');
    }

    public string ConfiguredSender => _options.FromNumber;

    // --- Lookup API (twilio_lookups_v2) ----------------------------------

    public async Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // GET /v2/PhoneNumbers/{PhoneNumber} — default fields include `valid` and canonical `phone_number`.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        // Twilio returns 404 for numbers it cannot even parse — treat that as "not a usable destination".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookup(false, null);
        }

        var payload = await ReadJsonAsync(response, "lookup a phone number", cancellationToken);
        var valid = payload.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = payload.TryGetProperty("phone_number", out var numberEl) && numberEl.ValueKind == JsonValueKind.String
            ? numberEl.GetString()
            : null;
        return new PhoneNumberLookup(valid, canonical);
    }

    // --- Messaging API (twilio_api_v2010) --------------------------------

    public async Task<GatewayMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // POST .../Messages.json — immediate send from the configured sender.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesCollectionUrl(), form, "send a message", cancellationToken);
    }

    public async Task<GatewayMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // POST .../Messages.json — scheduled send. Scheduling requires a Messaging Service and
        // ScheduleType=fixed with an ISO-8601 SendAt; From is omitted (assigned from the pool).
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["Body"] = body,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesCollectionUrl(), form, "schedule a message", cancellationToken);
    }

    public async Task<GatewayMessage> GetAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // GET .../Messages/{Sid}.json — current state, chiefly the delivery status.
        using var request = CreateRequest(HttpMethod.Get, MessageInstanceUrl(providerMessageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync(response, "fetch a message", cancellationToken);
        return ParseMessage(payload);
    }

    public async Task<GatewayMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // POST .../Messages/{Sid}.json with Status=canceled — cancels a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageInstanceUrl(providerMessageSid), form, "cancel a scheduled message", cancellationToken);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // POST .../Messages/{Sid}.json with an empty Body — redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageInstanceUrl(providerMessageSid), form, "dispose of message content", cancellationToken);
    }

    public async Task<IReadOnlyList<GatewayMessage>> ListSentByConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // GET .../Messages.json?From=<sender>&DateSent>=<from>&DateSent<=<to> — the sender filter is
        // applied by the provider (From), not after the fact. Paginate the whole range.
        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_options.FromNumber));
        query.Append("&DateSent%3E=").Append(Uri.EscapeDataString(FormatDateSent(from)));
        query.Append("&DateSent%3C=").Append(Uri.EscapeDataString(FormatDateSent(to)));
        query.Append("&PageSize=1000");

        var nextUrl = MessagesCollectionUrl() + query;
        var results = new List<GatewayMessage>();

        for (var page = 0; page < MaxReconciliationPages && nextUrl is not null; page++)
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await ReadJsonAsync(response, "list messages", cancellationToken);

            if (payload.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(message));
                }
            }

            // next_page_uri is relative to the messaging host; prepend the (possibly overridden) base.
            nextUrl = null;
            if (payload.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var relative = next.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    nextUrl = _messagingBaseUrl + relative;
                }
            }
        }

        return results;
    }

    // --- helpers ---------------------------------------------------------

    private string MessagesCollectionUrl() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageInstanceUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    private static string FormatDateSent(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);
        return request;
    }

    private async Task<GatewayMessage> PostMessageAsync(string url, Dictionary<string, string> form, string action, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await ReadJsonAsync(response, action, cancellationToken);
        return ParseMessage(payload);
    }

    private async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, content, action);
        }

        using var document = JsonDocument.Parse(content);
        return document.RootElement.Clone();
    }

    private TwilioApiException BuildApiException(HttpStatusCode status, string content, string action)
    {
        int? code = null;
        var message = $"Twilio request to {action} failed with HTTP {(int)status}.";
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
            }
            if (root.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String)
            {
                message = $"Twilio could not {action}: {messageEl.GetString()} (code {code}).";
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message. The body is not logged (may hold PII).
        }

        // Log the outcome without the request URL or body, both of which can contain phone numbers.
        _logger.LogWarning($"Twilio API error while trying to {action}: HTTP {(int)status}, code {code}.");
        return new TwilioApiException((int)status, code, message);
    }

    private static GatewayMessage ParseMessage(JsonElement element)
    {
        return new GatewayMessage(
            Sid: GetString(element, "sid") ?? string.Empty,
            Status: GetString(element, "status"),
            From: GetString(element, "from"),
            To: GetString(element, "to"),
            DateSent: ParseDate(GetString(element, "date_sent")),
            ErrorCode: GetInt(element, "error_code"),
            ErrorMessage: GetString(element, "error_message"),
            Body: GetString(element, "body"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio timestamps are RFC 2822, e.g. "Fri, 24 May 2019 17:44:50 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
