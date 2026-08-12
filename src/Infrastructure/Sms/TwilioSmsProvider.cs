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

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to the Twilio REST API over HTTP exactly as documented: form-encoded (PascalCase) request
/// bodies, JSON (snake_case) responses, HTTP Basic auth (AccountSid:AuthToken). Messaging calls go to
/// <see cref="TwilioOptions.BaseUrl"/> when set (else api.twilio.com); lookup always uses lookups.twilio.com.
/// The auth token is never logged; the shopper's number is never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, TwilioOptions options, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : options.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.AccountSid}:{options.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string SenderNumber => _options.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 is served from its own host and is not governed by the messaging BaseUrl override.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A malformed request (e.g. an unroutable path) is distinct from a number that is merely
            // invalid — the latter still returns 200 with valid=false.
            var (code, message) = ReadError(payload, response.StatusCode);
            throw new SmsProviderException($"Lookup failed with provider code {code}.", code, Sanitize(message, phoneNumber));
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");
        var national = GetString(root, "national_format");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
            foreach (var e in errEl.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);

        return new PhoneValidationResult(valid, canonical, national, errors);
    }

    public async Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var scheduled = sendAt.HasValue;

        var (status, payload) = await CreateMessageAsync(toPhoneNumber, body, sendAt, includeFrom: true, cancellationToken);

        // Scheduling requires a Messaging Service; if pinning a specific sender is not accepted for a
        // scheduled send, let the service pick the sender from its pool.
        if (scheduled && status == HttpStatusCode.BadRequest)
        {
            _logger.LogWarning("Scheduled send rejected with a pinned sender; retrying letting the messaging service choose the sender.");
            (status, payload) = await CreateMessageAsync(toPhoneNumber, body, sendAt, includeFrom: false, cancellationToken);
        }

        if (status is HttpStatusCode.Created or HttpStatusCode.OK)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return new SmsSendResult(
                GetString(root, "sid"),
                GetString(root, "status") ?? "queued",
                GetInt(root, "error_code"),
                Sanitize(GetString(root, "error_message"), toPhoneNumber),
                scheduled ? sendAt : null);
        }

        var (code, message) = ReadError(payload, status);
        return new SmsSendResult(null, "send_failed", code, Sanitize(message, toPhoneNumber), null);
    }

    public async Task<SmsMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ReadError(payload, response.StatusCode);
            throw new SmsProviderException($"Fetch message failed with provider code {code}.", code, message);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsMessageStatus(
            GetString(root, "status") ?? string.Empty,
            GetInt(root, "error_code"),
            GetString(root, "error_message"));
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };

        // A just-scheduled message can briefly be Not Found (20404) at the Messages resource before it
        // is cancelable. Cancelling must not be lost to that race, so retry a not-found for a short while.
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            var (status, payload) = await PostFormAsync(url, form, cancellationToken);
            if (status is HttpStatusCode.OK or HttpStatusCode.Created)
                return;

            var (code, message) = ReadError(payload, status);
            var transientNotFound = status == HttpStatusCode.NotFound || code == 20404;
            if (transientNotFound && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken);
                continue;
            }

            throw new SmsProviderException($"Cancel scheduled message failed with provider code {code}.", code, message);
        }
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redaction: POST the message with an empty Body removes the stored text at the provider,
        // while the resource (and its status) survives.
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var (status, payload) = await PostFormAsync(url, form, cancellationToken);
        if (status is not (HttpStatusCode.OK or HttpStatusCode.Created))
        {
            var (code, message) = ReadError(payload, status);
            throw new SmsProviderException($"Redact message failed with provider code {code}.", code, message);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListOutboundMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Ask the provider only for this application's own sending number, over the range. The
        // comparison operator is part of the parameter name and must be percent-encoded (%3E = '>',
        // %3C = '<'). Full ISO-8601 date-times are honoured, so the window is applied precisely.
        var fromStamp = fromUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toStamp = toUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var query =
            $"From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={fromStamp}" +  // DateSent> (lower bound)
            $"&DateSent%3C={toStamp}" +    // DateSent< (upper bound)
            "&PageSize=1000";

        var nextUri = $"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json?{query}";
        var results = new List<ProviderMessage>();

        while (!string.IsNullOrEmpty(nextUri))
        {
            var url = nextUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextUri
                : $"{_messagingBaseUrl}{nextUri}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ReadError(payload, response.StatusCode);
                throw new SmsProviderException($"List messages failed with provider code {code}.", code, message);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = GetString(m, "sid");
                    if (sid is null)
                        continue;

                    // "Messages sent from" our number means outbound messages the app actually sent.
                    // Inbound traffic on the same number, and not-yet-sent (scheduled/canceled)
                    // messages with no date_sent, are excluded.
                    var direction = GetString(m, "direction");
                    if (direction is null || !direction.StartsWith("outbound", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var dateSent = ParseRfc2822(GetString(m, "date_sent"));
                    if (!dateSent.HasValue || dateSent.Value < fromUtc || dateSent.Value > toUtc)
                        continue;

                    results.Add(new ProviderMessage(
                        sid,
                        GetString(m, "from"),
                        GetString(m, "to"),
                        GetString(m, "status") ?? string.Empty,
                        direction,
                        GetInt(m, "error_code"),
                        dateSent));
                }
            }

            nextUri = GetString(root, "next_page_uri");
        }

        return results;
    }

    private async Task<(HttpStatusCode Status, string Payload)> CreateMessageAsync(
        string toPhoneNumber, string body, DateTimeOffset? sendAt, bool includeFrom, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["Body"] = body
        };

        if (sendAt.HasValue)
        {
            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = sendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        if (includeFrom)
            form["From"] = _options.FromNumber;

        return await PostFormAsync(url, form, cancellationToken);
    }

    private async Task<(HttpStatusCode Status, string Payload)> PostFormAsync(string url, IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.StatusCode, payload);
    }

    private static (int? Code, string? Message) ReadError(string payload, HttpStatusCode httpStatus)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return (GetInt(root, "code"), GetString(root, "message"));
        }
        catch
        {
            return (null, $"HTTP {(int)httpStatus}");
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    // Keep the shopper's number out of any provider diagnostic we store or surface.
    private static string? Sanitize(string? message, string phoneNumber)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(phoneNumber))
            return message;
        return message
            .Replace(phoneNumber, "<redacted>", StringComparison.Ordinal)
            .Replace(Uri.EscapeDataString(phoneNumber), "<redacted>", StringComparison.Ordinal);
    }
}
