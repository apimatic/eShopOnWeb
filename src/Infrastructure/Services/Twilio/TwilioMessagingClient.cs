using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's REST API over HTTP with Basic authentication. The messaging API (send, read,
/// reconcile, redact, cancel) is reached through <c>Twilio:BaseUrl</c> when configured, otherwise the
/// provider default. The Lookup API is always reached from its own host and is unaffected by BaseUrl.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const int MaxRetries = 5;

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient http, TwilioSettings settings, IAppLogger<TwilioMessagingClient> logger)
    {
        _http = http;
        _settings = settings;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl!.TrimEnd('/');

        if (!string.IsNullOrEmpty(settings.AccountSid) && !string.IsNullOrEmpty(settings.AuthToken))
        {
            var credential = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credential);
        }
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        // Lookup v2, Basic (free) — validation + canonical form. Served from its own host, not BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";

        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);

        // A malformed / unroutable number is not a usable destination — treat as invalid rather than throwing.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberLookupResult(false, null, null, new[] { "NOT_A_NUMBER" });
        }

        await EnsureSuccessAsync(response, "lookup phone number", cancellationToken);

        using var document = await ParseJsonAsync(response, cancellationToken);
        var root = document.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");
        var national = GetString(root, "national_format");
        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errorsEl) && errorsEl.ValueKind == JsonValueKind.Array)
        {
            errors.AddRange(errorsEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0));
        }

        return new PhoneNumberLookupResult(valid, canonical, national, errors);
    }

    public async Task<ProviderMessage> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
        }

        // Scheduling is a Messaging Service capability: ScheduleType=fixed + SendAt, sender chosen from the pool.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
        await EnsureSuccessAsync(response, "fetch message", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        // Update the message with Status=canceled — the only value that parameter accepts.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await UpdateMessageAsync(messageSid, form, "cancel scheduled message", cancellationToken);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();
        // Redact the body by updating it to an empty string; the resource and its outcome survive.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await UpdateMessageAsync(messageSid, form, "redact message body", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        EnsureCredentials();

        // Ask the provider for the configured sending number's messages directly (From filter applied there),
        // narrowed to the range's days (inclusive), then follow every page so the whole range is covered.
        var fromDate = fromUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = string.Join('&', new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            // DateSent> and DateSent< are the provider's inequality filter parameter names (keys url-encoded).
            $"{Uri.EscapeDataString("DateSent>")}={fromDate}",
            $"{Uri.EscapeDataString("DateSent<")}={toDate}",
            "PageSize=1000"
        });

        var messages = new List<ProviderMessage>();
        string? path = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json?{query}";

        while (!string.IsNullOrEmpty(path))
        {
            var url = _messagingBaseUrl + path;
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url), cancellationToken);
            await EnsureSuccessAsync(response, "list messages", cancellationToken);
            using var document = await ParseJsonAsync(response, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(messagesEl.EnumerateArray().Select(ParseMessage));
            }

            path = root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String
                ? nextEl.GetString()
                : null;
        }

        return messages;
    }

    // ----- internals -----

    private async Task<ProviderMessage> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        }, cancellationToken);
        await EnsureSuccessAsync(response, "create message", cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    private async Task<ProviderMessage> UpdateMessageAsync(string messageSid, Dictionary<string, string> form, string action, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        }, cancellationToken);
        await EnsureSuccessAsync(response, action, cancellationToken);
        using var document = await ParseJsonAsync(response, cancellationToken);
        return ParseMessage(document.RootElement);
    }

    /// <summary>
    /// Sends a request, retrying transient failures (429 and 5xx) with exponential backoff and full
    /// jitter, honoring a Retry-After header when the provider supplies one.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            response?.Dispose();

            using var request = requestFactory();
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaxRetries - 1)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken);
                continue;
            }

            if (!IsTransient(response.StatusCode) || attempt == MaxRetries - 1)
            {
                return response;
            }

            await DelayBeforeRetryAsync(attempt, response.Headers.RetryAfter?.Delta, cancellationToken);
        }

        return response!;
    }

    private static async Task DelayBeforeRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        if (retryAfter is { } wait)
        {
            await Task.Delay(wait, cancellationToken);
            return;
        }

        // Full jitter: sleep a random amount up to an exponentially growing window (base 500ms, cap 30s).
        var window = Math.Min(30_000d, 500d * Math.Pow(2, attempt));
        var delay = Random.Shared.NextDouble() * window;
        await Task.Delay(TimeSpan.FromMilliseconds(delay), cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured (Twilio:AccountSid / Twilio:AuthToken).");
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Surface the provider's own error message (which never contains the auth token) without a destination number.
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = ExtractProviderError(payload);
        _logger.LogWarning("Twilio request to {Action} failed with {StatusCode}: {Detail}", action, (int)response.StatusCode, detail);
        throw new HttpRequestException($"Twilio request to {action} failed with status {(int)response.StatusCode}: {detail}");
    }

    private static string ExtractProviderError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return "no response body";
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var message = GetString(root, "message");
            var code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32().ToString(CultureInfo.InvariantCulture)
                : null;
            if (!string.IsNullOrEmpty(message))
            {
                return code is null ? message : $"{message} (code {code})";
            }
        }
        catch (JsonException)
        {
            // fall through to raw payload
        }

        return payload.Length > 500 ? payload[..500] : payload;
    }

    private static async Task<JsonDocument> ParseJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static ProviderMessage ParseMessage(JsonElement element) => new(
        Sid: GetString(element, "sid"),
        Status: GetString(element, "status"),
        ErrorCode: GetInt(element, "error_code"),
        ErrorMessage: GetString(element, "error_message"),
        From: GetString(element, "from"),
        To: GetString(element, "to"),
        DateSent: GetDate(element, "date_sent"),
        DateCreated: GetDate(element, "date_created"),
        Price: GetString(element, "price"),
        NumSegments: GetString(element, "num_segments"));

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
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

        // Twilio timestamps are RFC 2822 (e.g. "Fri, 24 May 2019 17:44:46 +0000").
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;
    }
}
