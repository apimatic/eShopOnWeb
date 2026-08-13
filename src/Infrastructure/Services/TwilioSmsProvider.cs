using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// <see cref="ISmsProvider"/> implemented against the provider's REST API over HTTP.
///
/// The messaging calls (create, fetch, update/cancel/redact, list) go to the messaging host —
/// the configured <see cref="TwilioSettings.BaseUrl"/> override when present, otherwise the
/// provider default. Number lookup is a separate host and is not affected by that override.
///
/// The classic messaging API takes <c>application/x-www-form-urlencoded</c> bodies with
/// PascalCase parameters and returns snake_case JSON. Recipient numbers and message bodies are
/// treated as PII: they are never logged, and provider error responses (which can echo the number
/// back) are reduced to an HTTP status and the provider's numeric error code before anything is
/// written to a log or an exception message.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string FormMediaType = "application/x-www-form-urlencoded";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    /// <summary>Messaging host: the override verbatim when set, otherwise the provider default.</summary>
    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesResourceUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageResourceUrl(string sid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, string? countryCode,
        CancellationToken cancellationToken)
    {
        // Pass the raw input through to the provider's lookup, letting it normalise; do not pre-clean.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // v2 returns 200 with valid:false for a number that is simply not usable. A 400/404 is also
        // treated as "not a usable destination" rather than an integration failure.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
            return new PhoneNumberValidation(false, null, null, null, Array.Empty<string>());

        if (!response.IsSuccessStatusCode)
            throw ProviderError("number lookup", response.StatusCode, payload);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var isValid = root.TryGetProperty("valid", out var validEl)
            && validEl.ValueKind == JsonValueKind.True;

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
            errors.AddRange(errEl.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0));

        return new PhoneNumberValidation(
            isValid,
            isValid ? GetString(root, "phone_number") : null,
            isValid ? GetString(root, "national_format") : null,
            isValid ? GetString(root, "country_code") : null,
            errors);
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        using var response = await PostFormAsync(MessagesResourceUrl, form, cancellationToken);
        return await ReadMessageResultAsync("send message", response, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt,
        CancellationToken cancellationToken)
    {
        // Scheduling requires a Messaging Service; SendAt is ISO-8601 UTC and must be 15 min–35 days out.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        using var response = await PostFormAsync(MessagesResourceUrl, form, cancellationToken);
        return await ReadMessageResultAsync("schedule message", response, cancellationToken);
    }

    public async Task<SmsSendResult> FetchAsync(string messageSid, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => new HttpRequestMessage(HttpMethod.Get, MessageResourceUrl(messageSid)), cancellationToken);
        return await ReadMessageResultAsync("fetch message", response, cancellationToken);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await PostFormAsync(MessageResourceUrl(messageSid), form, cancellationToken);
        await EnsureSuccessAsync("cancel scheduled message", response, cancellationToken);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken)
    {
        // Redaction: update the message with an empty Body so the text is no longer retrievable.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync(MessageResourceUrl(messageSid), form, cancellationToken);
        await EnsureSuccessAsync("redact message body", response, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        // Ask the provider directly for this sending number's messages in the window. The DateSent
        // filter is date-granular, so widen to whole-day bounds to be sure the range is fully
        // covered; the caller narrows to the exact datetimes afterwards.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={fromDate}" +   // DateSent>= floor(from)
                    $"&DateSent%3C={toDate}" +     // DateSent<= ceil(to)
                    $"&PageSize=1000";
        var nextUrl = $"{MessagesResourceUrl}?{query}";

        var results = new List<ProviderMessage>();
        var safetyPageCap = 1000; // hard stop against a pathological paging loop

        while (nextUrl is not null && safetyPageCap-- > 0)
        {
            using var response = await SendWithRetryAsync(
                () => new HttpRequestMessage(HttpMethod.Get, nextUrl), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw ProviderError("list messages", response.StatusCode, payload);

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var sid = GetString(message, "sid");
                    if (string.IsNullOrEmpty(sid))
                        continue;

                    results.Add(new ProviderMessage(
                        sid!,
                        GetString(message, "from"),
                        GetString(message, "status") ?? string.Empty,
                        ParseProviderDate(GetString(message, "date_sent")),
                        GetInt(message, "error_code"),
                        HasBody(message)));
                }
            }

            // Classic API: next_page_uri is a relative path resolved against the messaging host.
            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : $"{MessagingBaseUrl}{next}";
        }

        return results;
    }

    // ----- HTTP helpers -------------------------------------------------------------------------

    private Task<HttpResponseMessage> PostFormAsync(string url, IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        // A create/update is not retried: a transient-looking failure could still have sent, and a
        // blind retry would risk a duplicate message.
        return _httpClient.SendAsync(BuildFormRequest(url, form), cancellationToken);
    }

    private static HttpRequestMessage BuildFormRequest(string url, IReadOnlyDictionary<string, string> form)
    {
        // Encode the form by hand so a '+' in an E.164 number survives as %2B rather than a space.
        var encoded = string.Join("&", form.Select(kvp =>
            $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(encoded, Encoding.UTF8, FormMediaType)
        };
    }

    /// <summary>Sends an idempotent (GET/read) request, retrying briefly on 429/5xx.</summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            var response = await _httpClient.SendAsync(requestFactory(), cancellationToken);
            var transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
            if (!transient || attempt == maxAttempts)
                return response;

            response.Dispose();
            await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
        }
    }

    private async Task<SmsSendResult> ReadMessageResultAsync(string operation, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw ProviderError(operation, response.StatusCode, payload);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = GetString(root, "sid");
        if (string.IsNullOrEmpty(sid))
            throw new SmsProviderException($"Provider {operation} response carried no message sid.",
                (int)response.StatusCode);

        return new SmsSendResult(sid!, GetString(root, "status") ?? string.Empty, GetInt(root, "error_code"));
    }

    private async Task EnsureSuccessAsync(string operation, HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        throw ProviderError(operation, response.StatusCode, payload);
    }

    /// <summary>
    /// Builds a PII-free <see cref="SmsProviderException"/> from a failed response: extracts the
    /// provider's numeric error code but never the human-readable message (which can contain the
    /// recipient number), logs the status and code only, and returns the exception to throw.
    /// </summary>
    private SmsProviderException ProviderError(string operation, HttpStatusCode statusCode, string payload)
    {
        int? providerCode = null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            providerCode = GetInt(doc.RootElement, "code");
        }
        catch (JsonException)
        {
            // Non-JSON error body — ignore it entirely rather than risk logging its contents.
        }

        _logger.LogWarning("Provider {Operation} failed: httpStatus={HttpStatus} providerCode={ProviderCode}.",
            operation, (int)statusCode, providerCode);

        return new SmsProviderException(
            $"Provider {operation} failed (HTTP {(int)statusCode}, code {providerCode?.ToString() ?? "n/a"}).",
            (int)statusCode, providerCode);
    }

    // ----- JSON helpers -------------------------------------------------------------------------

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static bool HasBody(JsonElement message) =>
        message.TryGetProperty("body", out var body)
        && body.ValueKind == JsonValueKind.String
        && !string.IsNullOrEmpty(body.GetString());

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
