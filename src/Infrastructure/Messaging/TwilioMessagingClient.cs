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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Talks to Twilio over plain HTTP (no SDK) so that the messaging base address can be overridden
/// verbatim per configuration. Number validation uses the Lookup API on its own host, which the
/// override does not govern. Nothing here ever logs a phone number, a message body, or the token.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private static readonly string LookupBase =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;
    private readonly string _messagingBase;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBase
            : _settings.BaseUrl!.TrimEnd('/');

        // HTTP Basic auth: AccountSid:AuthToken. The token is never logged.
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
    }

    private string MessagesEndpoint => $"{_messagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageEndpoint(string sid) => $"{_messagingBase}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 lives on lookups.twilio.com regardless of the messaging BaseUrl override.
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup returns 404 for a number it cannot parse at all: not a usable destination.
            return new PhoneLookupResult(false, null);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException("Lookup", response.StatusCode, body);
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;
        return new PhoneLookupResult(valid, canonical);
    }

    public async Task<ProviderMessage> SendMessageAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // Send immediately from the configured number so the message is attributable to it during
        // reconciliation (which filters by From).
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        var message = await PostMessageAsync(MessagesEndpoint, form, "send", cancellationToken);
        _logger.LogInformation("Twilio message created sid={Sid} status={Status}", message.Sid, message.Status);
        return message;
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string toE164, string body, DateTimeOffset sendAt,
        CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service SID (Twilio does not allow From-only scheduling).
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        var message = await PostMessageAsync(MessagesEndpoint, form, "schedule", cancellationToken);
        _logger.LogInformation("Twilio message scheduled sid={Sid} status={Status}", message.Sid, message.Status);
        return message;
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var message = await PostMessageAsync(MessageEndpoint(providerMessageSid), form, "cancel", cancellationToken);
        _logger.LogInformation("Twilio message canceled sid={Sid} status={Status}", message.Sid, message.Status);
        return message;
    }

    public async Task<ProviderMessage> RedactMessageBodyAsync(string providerMessageSid,
        CancellationToken cancellationToken = default)
    {
        // Redaction: POST an empty Body. The record and its outcome survive; the text is removed.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var message = await PostMessageAsync(MessageEndpoint(providerMessageSid), form, "redact", cancellationToken);
        _logger.LogInformation("Twilio message body redacted sid={Sid}", message.Sid);
        return message;
    }

    public async Task<ProviderMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageEndpoint(providerMessageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException("GetMessage", response.StatusCode, body);
        }
        using var doc = JsonDocument.Parse(body);
        return ParseMessage(doc.RootElement);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromE164, DateTimeOffset dateSentAfterUtc,
        DateTimeOffset dateSentBeforeUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this number's messages within the day range covering [from,to].
        // Twilio's DateSent filters are day-granular and inclusive, so flooring/ceiling to whole
        // days guarantees the whole requested range is covered.
        var afterDay = dateSentAfterUtc.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var beforeDay = dateSentBeforeUtc.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"?From={Uri.EscapeDataString(fromE164)}" +
                    $"&DateSent%3E={afterDay}" +   // DateSent>  (on or after)
                    $"&DateSent%3C={beforeDay}" +  // DateSent<  (on or before)
                    "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUrl = MessagesEndpoint + query;
        var pages = 0;

        while (nextUrl is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException("ListMessages", response.StatusCode, body);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(msg));
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String)
            {
                var nextPath = next.GetString();
                if (!string.IsNullOrEmpty(nextPath))
                {
                    // next_page_uri is an absolute path; resolve it against the messaging host.
                    nextUrl = new Uri(new Uri(_messagingBase), nextPath).ToString();
                }
            }

            if (++pages > 1000) break; // safety valve against a pathological pagination loop
        }

        _logger.LogInformation("Twilio reconciliation listed {Count} message(s) across {Pages} page(s)", results.Count, pages);
        return results;
    }

    private async Task<ProviderMessage> PostMessageAsync(string url, IDictionary<string, string> form,
        string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(operation, response.StatusCode, body);
        }
        using var doc = JsonDocument.Parse(body);
        return ParseMessage(doc.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement el)
    {
        string sid = GetString(el, "sid") ?? string.Empty;
        string status = GetString(el, "status") ?? string.Empty;
        int? errorCode = GetInt(el, "error_code");
        string? to = GetString(el, "to");
        string? from = GetString(el, "from");
        string? body = GetString(el, "body");
        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(el, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }
        return new ProviderMessage(sid, status, errorCode, to, from, body, dateSent);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return i;
        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    /// <summary>
    /// Builds an exception carrying only the provider error code — never the response body, which
    /// may echo the destination number.
    /// </summary>
    private TwilioApiException BuildApiException(string operation, HttpStatusCode statusCode, string responseBody)
    {
        int? code = null;
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            code = GetInt(doc.RootElement, "code");
        }
        catch (JsonException)
        {
            // non-JSON error body; ignore, we surface neither its content nor any number
        }

        _logger.LogWarning("Twilio {Operation} failed httpStatus={HttpStatus} providerCode={Code}",
            operation, (int)statusCode, code?.ToString() ?? "n/a");
        return new TwilioApiException($"Twilio {operation} failed (HTTP {(int)statusCode}).", code);
    }
}
