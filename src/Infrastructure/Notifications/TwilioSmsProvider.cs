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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Talks to Twilio's REST APIs over HTTP, exactly as documented: the messaging Message resource on
/// the 2010-04-01 API for sending, reading, cancelling, redacting and listing messages, and the
/// Lookup v2 API for validating a destination number.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;
    private readonly AuthenticationHeaderValue _authHeader;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;

        // The BaseUrl override, when present, governs every messaging-API call verbatim. The lookup
        // API is served from its own host and is not affected by it.
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

        // HTTP Basic: Account SID as username, Auth Token as password. Built once, never logged.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _authHeader = new AuthenticationHeaderValue("Basic", basic);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Free Lookup v2 basic validation (no Fields requested). Served from the lookup host.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var document = await SendAsync(request, cancellationToken);
        var root = document.RootElement;

        // An invalid number is not an error: it comes back 200 with valid=false and reasons.
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            var errors = new List<string>();
            if (root.TryGetProperty("validation_errors", out var errsEl) && errsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errsEl.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        errors.Add(e.GetString()!);
                    }
                }
            }

            return PhoneNumberValidation.Invalid(errors);
        }

        // Store the provider's canonical E.164 form, not the raw input.
        var canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        return string.IsNullOrEmpty(canonical)
            ? PhoneNumberValidation.Invalid(new[] { "the provider returned no canonical number" })
            : PhoneNumberValidation.Valid(canonical);
    }

    public async Task<ProviderMessage> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt is { } sendAt)
        {
            // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
            form["MessagingServiceSid"] = _settings.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else
        {
            // Immediate send from this application's own configured number, so every sent message is
            // attributable to Twilio:FromNumber for reconciliation.
            form["From"] = _settings.FromNumber;
        }

        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var document = await SendAsync(httpRequest, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var document = await SendAsync(request, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Cancelling a not-yet-sent message: POST the Message with Status=canceled (the only value
        // that parameter accepts).
        //
        // A message accepted for scheduling a moment ago is briefly not yet retrievable/cancelable
        // and answers 404 (error 20404). Because calling off a follow-up before it reaches a shopper
        // is the whole point of this call, we retry through that eventual-consistency window rather
        // than give up on the first 404.
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";

        const int maxAttempts = 6;
        var delay = TimeSpan.FromSeconds(2);
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" })
            };

            try
            {
                using var _ = await SendAsync(request, cancellationToken);
                return;
            }
            catch (TwilioApiException ex) when (ex.HttpStatus == System.Net.HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redacting the body: POST the Message with an empty Body. The record and its delivery
        // outcome survive; only the text is removed at the provider.
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var _ = await SendAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own number's messages within the range, rather than
        // filtering a wider answer after the fact. The DateSent bounds are day-granular: the provider
        // treats a bare date as midnight, so DateSent< is set to the day AFTER `to` to include every
        // message on `to`'s own day. The exact-time window is then refined client-side below.
        var fromDate = from.ToUniversalTime().Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={fromDate}" +
            $"&{Uri.EscapeDataString("DateSent<")}={toDate}" +
            "&PageSize=1000";
        var nextUrl = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}";

        var results = new List<ProviderMessage>();

        // Follow pagination so the whole range is covered.
        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var document = await SendAsync(request, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var messageEl in messagesEl.EnumerateArray())
                {
                    var message = ParseMessage(messageEl);
                    // Refine to the exact requested window (the provider filter is day-granular).
                    if (message.DateSent is null || (message.DateSent >= from && message.DateSent <= to))
                    {
                        results.Add(message);
                    }
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var relative = nextEl.GetString();
                if (!string.IsNullOrEmpty(relative))
                {
                    // next_page_uri is relative to the messaging host.
                    nextUrl = $"{_messagingBaseUrl}{relative}";
                }
            }
        }

        return results;
    }

    // ----- HTTP plumbing ---------------------------------------------------------------------

    private async Task<JsonDocument> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = _authHeader;
        request.Headers.Accept.ParseAdd("application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToApiException(response.StatusCode, body);
        }

        // Some successful calls (e.g. a 204 delete) have no body; hand back an empty object.
        if (string.IsNullOrWhiteSpace(body))
        {
            return JsonDocument.Parse("{}");
        }

        return JsonDocument.Parse(body);
    }

    private static TwilioApiException ToApiException(System.Net.HttpStatusCode status, string body)
    {
        int? code = null;
        string message = "the provider returned an error";
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                code = codeEl.GetInt32();
            }

            if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                message = msgEl.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message rather than surfacing raw content.
        }

        return new TwilioApiException(status, code, message);
    }

    private static ProviderMessage ParseMessage(JsonElement element)
    {
        var sid = GetString(element, "sid") ?? string.Empty;
        var to = GetString(element, "to");
        var fromNumber = GetString(element, "from");
        var status = GetString(element, "status") ?? "unknown";

        int? errorCode = null;
        if (element.TryGetProperty("error_code", out var errEl) && errEl.ValueKind == JsonValueKind.Number)
        {
            errorCode = errEl.GetInt32();
        }

        var errorMessage = GetString(element, "error_message");

        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(element, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessage(sid, to, fromNumber, status, errorCode, errorMessage, dateSent);
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
