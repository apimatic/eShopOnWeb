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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A hand-written Twilio client built directly against the Twilio OpenAPI specification (the authoritative
/// contract). It targets two documents: Lookups v2 (<c>/v2/PhoneNumbers/{PhoneNumber}</c>) for number
/// validation, and the Api 2010-04-01 Message resource (<c>/2010-04-01/Accounts/{AccountSid}/Messages.json</c>
/// and <c>.../Messages/{Sid}.json</c>) for sending, scheduling, fetching, cancelling, redacting and listing.
/// Authentication is HTTP Basic (AccountSid:AuthToken) as declared by the spec's <c>accountSid_authToken</c>
/// security scheme. No phone number, message body or auth token is ever written to logs.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string MessagesApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _messagingBaseUrl = _settings.EffectiveMessagingBaseUrl.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    // ----- Lookups v2 -----------------------------------------------------------------------------

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber} (not governed by Twilio:BaseUrl).
        var url = $"{TwilioSettings.LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // The provider answers 404 for a number it cannot recognise at all; treat that as "not usable".
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_FOUND" });
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body, "phone-number lookup failed");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);
            }
        }

        return new PhoneNumberLookupResult(valid, canonical, errors);
    }

    // ----- Message resource (Api 2010-04-01) ------------------------------------------------------

    public Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service capability: ScheduleType=fixed + SendAt, no From (the service
        // picks the sender). Twilio queues and sends it later — this application holds no timer of its own.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<TwilioMessageResource> CreateMessageAsync(
        IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/{MessagesApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var respBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, respBody, "message create failed");
        }
        return MapMessage(respBody);
    }

    public async Task<TwilioMessageResource> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/{MessagesApiVersion}/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body, "message fetch failed");
        }
        return MapMessage(body);
    }

    public Task<TwilioMessageResource> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new[] { new KeyValuePair<string, string>("Status", "canceled") }, "message cancel failed", cancellationToken);

    public Task<TwilioMessageResource> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new[] { new KeyValuePair<string, string>("Body", string.Empty) }, "message redact failed", cancellationToken);

    private async Task<TwilioMessageResource> UpdateMessageAsync(
        string messageSid, IEnumerable<KeyValuePair<string, string>> form, string failureContext, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/{MessagesApiVersion}/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildException(response.StatusCode, body, failureContext);
        }
        return MapMessage(body);
    }

    public async Task<IReadOnlyList<TwilioMessageResource>> ListByFromAsync(
        string fromNumber, DateTimeOffset fromDateUtc, DateTimeOffset toDateUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this sending number's messages (spec: From filter), bounded by the
        // GMT sent-date range (DateSent> / DateSent<), and page through the whole range via next_page_uri.
        // Twilio's DateSent bounds are whole GMT days pinned to midnight, so DateSent<=D excludes messages
        // sent later on day D. Widen the upper bound to the following day and trim to the exact instant
        // requested with the in-memory filter below, so the whole range is genuinely covered.
        var fromDate = fromDateUtc.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toDateUtc.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"From={Uri.EscapeDataString(fromNumber)}" +
                    $"&DateSent%3E={fromDate}" +
                    $"&DateSent%3C={toDate}" +
                    "&PageSize=1000";

        var nextUrl = $"{_messagingBaseUrl}/{MessagesApiVersion}/Accounts/{_settings.AccountSid}/Messages.json?{query}";

        var results = new List<TwilioMessageResource>();
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildException(response.StatusCode, body, "message list failed");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messagesEl.EnumerateArray())
                {
                    results.Add(MapMessage(m));
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var relative = nextEl.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    // next_page_uri is relative to the API host; resolve it against the messaging base URL.
                    nextUrl = $"{_messagingBaseUrl}{relative}";
                }
            }
        }

        return results;
    }

    // ----- helpers --------------------------------------------------------------------------------

    private static TwilioMessageResource MapMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return MapMessage(doc.RootElement);
    }

    private static TwilioMessageResource MapMessage(JsonElement el)
    {
        int? errorCode = null;
        if (el.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.TryGetInt32(out var code))
            errorCode = code;

        return new TwilioMessageResource(
            Sid: GetString(el, "sid") ?? string.Empty,
            Status: GetString(el, "status"),
            To: GetString(el, "to"),
            From: GetString(el, "from"),
            Body: GetString(el, "body"),
            ErrorCode: errorCode,
            ErrorMessage: GetString(el, "error_message"),
            DateSent: ParseDate(GetString(el, "date_sent")),
            DateCreated: ParseDate(GetString(el, "date_created")),
            DateUpdated: ParseDate(GetString(el, "date_updated")));
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        // Twilio serialises message timestamps in RFC 2822, e.g. "Fri, 24 May 2019 17:44:50 +0000".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dto))
            return dto;
        return null;
    }

    private static TwilioApiException BuildException(HttpStatusCode statusCode, string body, string context)
    {
        // Parse Twilio's error model (code/message) from the spec without echoing PII into logs.
        int? providerCode = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var c))
                providerCode = c;
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep just the status code.
        }

        var codePart = providerCode.HasValue ? $" (provider code {providerCode})" : string.Empty;
        return new TwilioApiException(statusCode, providerCode, $"Twilio {context}: HTTP {(int)statusCode}{codePart}.");
    }
}
