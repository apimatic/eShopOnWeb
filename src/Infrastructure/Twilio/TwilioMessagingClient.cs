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
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// A hand-written client for the Twilio HTTP APIs, built strictly to the OpenAPI documents in
/// <c>api-specs/twilio</c>:
/// <list type="bullet">
/// <item>Lookups v2 (<c>lookups.twilio.com</c>) for number validation and canonicalisation.</item>
/// <item>The 2010-04-01 Messaging API (<c>api.twilio.com</c>, or the configured override) for
/// sending, fetching, cancelling, redacting and listing messages.</item>
/// </list>
/// Auth is HTTP Basic (AccountSid:AuthToken) per the spec's <c>accountSid_authToken</c> scheme.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";
    private const int MaxTransientRetries = 3;
    private const int MaxListPages = 100;

    private readonly HttpClient _http;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioOptions> options)
    {
        _http = http;
        _options = options.Value;

        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBaseUrl : _options.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstanceUrl(string sid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string rawPhoneNumber, CancellationToken cancellationToken = default)
    {
        // GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}  (Lookups v2 — not governed by Twilio:BaseUrl)
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawPhoneNumber)}";

        using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Lookups returns 404 with valid:false semantics for some inputs; treat a 404 as "not a
            // usable destination" rather than a hard failure.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new PhoneNumberLookupResult { Valid = false, ValidationErrors = new List<string> { "NOT_FOUND" } };
            }
            throw ToApiException(response.StatusCode, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? phoneNumber = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrEmpty(value)) errors.Add(value);
                }
            }
        }

        return new PhoneNumberLookupResult { Valid = valid, PhoneNumber = phoneNumber, ValidationErrors = errors };
    }

    public async Task<TwilioMessageResource> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = _options.FromNumber
        };

        if (request.SendAt is not null)
        {
            // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUrl) { Content = new FormUrlEncodedContent(form) },
            cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, payload);
        }

        return ParseMessage(payload);
    }

    public async Task<TwilioMessageResource> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, MessageInstanceUrl(messageSid)), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, payload);
        }

        return ParseMessage(payload);
    }

    public Task<TwilioMessageResource> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessageResource> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default) =>
        // Per the spec, an empty Body redacts the message's text content at the provider.
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    private async Task<TwilioMessageResource> UpdateMessageAsync(string messageSid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, MessageInstanceUrl(messageSid)) { Content = new FormUrlEncodedContent(form) },
            cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, payload);
        }

        return ParseMessage(payload);
    }

    public async Task<IReadOnlyList<TwilioMessageResource>> ListMessagesFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider only for this application's own sending number's messages, over the range.
        // DateSent bounds are date-granular; use whole-day floor/ceil so the whole range is covered.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // %3E => '>' (DateSent>=, inclusive lower bound); %3C => '<' (DateSent<=, inclusive upper bound).
        var nextUrl = MessagesCollectionUrl +
            $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={fromDate}" +
            $"&DateSent%3C={toDate}" +
            "&PageSize=1000";

        var results = new List<TwilioMessageResource>();
        var pages = 0;

        while (!string.IsNullOrEmpty(nextUrl) && pages < MaxListPages)
        {
            pages++;
            var requestUrl = nextUrl;
            using var response = await SendAsync(() => new HttpRequestMessage(HttpMethod.Get, requestUrl), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw ToApiException(response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesEl.EnumerateArray())
                {
                    results.Add(ParseMessage(message));
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var relative = nextEl.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    // next_page_uri is relative to the messaging host; combine with the configured base.
                    nextUrl = MessagingBaseUrl + relative;
                }
            }
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 1; ; attempt++)
        {
            using var request = requestFactory();
            try
            {
                response = await _http.SendAsync(request, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxTransientRetries)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            if (IsTransient(response.StatusCode) && attempt < MaxTransientRetries)
            {
                response.Dispose();
                await DelayBeforeRetryAsync(attempt, cancellationToken);
                continue;
            }

            return response;
        }
    }

    private static Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken) =>
        Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);

    private static bool IsTransient(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static TwilioMessageResource ParseMessage(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        return ParseMessage(doc.RootElement);
    }

    private static TwilioMessageResource ParseMessage(JsonElement element) => new()
    {
        Sid = GetString(element, "sid") ?? string.Empty,
        Status = GetString(element, "status"),
        To = GetString(element, "to"),
        From = GetString(element, "from"),
        Body = GetString(element, "body"),
        ErrorCode = GetInt(element, "error_code"),
        ErrorMessage = GetString(element, "error_message"),
        DateSent = GetDate(element, "date_sent"),
        DateCreated = GetDate(element, "date_created"),
        MessagingServiceSid = GetString(element, "messaging_service_sid")
    };

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string name)
    {
        var raw = GetString(element, name);
        if (string.IsNullOrEmpty(raw)) return null;
        // Twilio serialises dates in RFC-2822 form, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static TwilioApiException ToApiException(HttpStatusCode statusCode, string payload)
    {
        int? code = null;
        string message = $"Twilio API returned {(int)statusCode} ({statusCode}).";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String)
            {
                message = messageEl.GetString() ?? message;
            }
            code = GetInt(root, "code");
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        return new TwilioApiException(statusCode, code, message);
    }
}
