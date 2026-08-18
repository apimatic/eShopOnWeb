using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Talks to Twilio over its documented REST API. Messaging (send/fetch/list/update/delete) goes
/// through the messaging host — overridable by <see cref="TwilioSettings.BaseUrl"/> — using the
/// classic <c>/2010-04-01</c> Messages resource. Number validation uses the Lookup v2 host, which
/// is a different host and is never governed by the messaging base-url override.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    public const string MessagingClientName = "twilio-messaging";
    public const string LookupClientName = "twilio-lookup";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;

    public TwilioSmsProvider(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    public string FromNumber => _settings.FromNumber;

    private string MessagesResourcePath => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageResourcePath(string sid) => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    // ---- validation (Lookup v2) -------------------------------------------------------------

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(LookupClientName);

        // The '+' of an E.164 number sits in the URL path and must be percent-encoded.
        var pathNumber = Uri.EscapeDataString((rawNumber ?? string.Empty).Trim());
        var url = $"v2/PhoneNumbers/{pathNumber}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await client.GetAsync(url, cancellationToken);

        // A malformed number produces a 404/400 route error rather than a 200 with valid:false;
        // treat that as an unusable number rather than an integration failure.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberValidationResult(false, null, null, null, new[] { "NOT_A_NUMBER" });
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(body);
            throw new TwilioApiException((int)response.StatusCode, code, $"Lookup failed: {message}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var e164 = GetString(root, "phone_number");
        var national = GetString(root, "national_format");
        var country = GetString(root, "country_code");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String) errors.Add(e.GetString()!);
            }
        }

        return new PhoneNumberValidationResult(valid, e164, national, country, errors);
    }

    // ---- send / schedule --------------------------------------------------------------------

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service feature. Pin the sender to our own From number (which
        // is in the service's sender pool) so scheduled traffic still reconciles by From.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(MessagesResourcePath, content, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(body);
            throw new TwilioApiException((int)response.StatusCode, code, $"Create message failed: {message}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var sid = GetString(root, "sid") ?? throw new TwilioApiException((int)response.StatusCode, null, "Create message returned no sid.");
        var status = GetString(root, "status") ?? "unknown";
        var errorCode = GetIntOrNull(root, "error_code");
        return new SmsSendResult(sid, status, errorCode);
    }

    // ---- fetch / update / delete ------------------------------------------------------------

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var response = await client.GetAsync(MessageResourcePath(messageSid), cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ParseError(body);
            throw new TwilioApiException((int)response.StatusCode, code, $"Fetch message failed: {message}");
        }

        using var doc = JsonDocument.Parse(body);
        return ReadMessage(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Update the Message with Status=canceled to call off a not-yet-sent scheduled message.
        await UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Update the Message with an empty Body to redact the text at the provider.
        await UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    private async Task UpdateMessageAsync(string messageSid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(MessageResourcePath(messageSid), content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, message) = ParseError(errorBody);
            throw new TwilioApiException((int)response.StatusCode, code, $"Update message failed: {message}");
        }
    }

    // ---- list / reconciliation --------------------------------------------------------------

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(MessagingClientName);

        // The DateSent list filter is day-granular. Widen by a day on each edge so the exact
        // window is fully covered; the caller narrows to the precise range afterwards.
        var fromDate = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Ask the provider only for our own sending number's messages (From filter), not a wider
        // answer filtered after the fact. The comparison operator is part of the parameter name.
        var query =
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={fromDate}" +   // DateSent>  -> on/after
            $"&DateSent%3C={toDate}" +     // DateSent<  -> on/before
            $"&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUri = MessagesResourcePath + query;

        while (!string.IsNullOrEmpty(nextUri))
        {
            using var response = await client.GetAsync(nextUri, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = ParseError(body);
                throw new TwilioApiException((int)response.StatusCode, code, $"List messages failed: {message}");
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(ReadMessage(m));
                }
            }

            // next_page_uri is a relative URI (or null); resolve it against the messaging host.
            nextUri = GetString(root, "next_page_uri");
        }

        return results;
    }

    // ---- json helpers -----------------------------------------------------------------------

    private static ProviderMessage ReadMessage(JsonElement m)
    {
        return new ProviderMessage(
            Sid: GetString(m, "sid") ?? string.Empty,
            Status: GetString(m, "status"),
            From: GetString(m, "from"),
            To: GetString(m, "to"),
            Direction: GetString(m, "direction"),
            DateSent: ParseDate(GetString(m, "date_sent")),
            ErrorCode: GetIntOrNull(m, "error_code"));
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString();
        }
        return null;
    }

    private static int? GetIntOrNull(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // The 2010-04-01 API returns RFC 2822 timestamps (e.g. "Thu, 24 Aug 2023 05:01:45 +0000").
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto;
        }
        return null;
    }

    private static (int? code, string message) ParseError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return (null, "(no response body)");
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var code = GetIntOrNull(root, "code");
            var message = GetString(root, "message") ?? "(no message)";
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, "(unparseable error response)");
        }
    }
}
