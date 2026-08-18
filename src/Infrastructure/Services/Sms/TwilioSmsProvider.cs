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
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/> over plain HTTP. Every messaging-API call goes
/// through <see cref="MessagingBase"/> (honoring the optional <c>Twilio:BaseUrl</c> override); the Lookup
/// call always goes to Twilio's dedicated lookups host, which the override does not govern.
///
/// Confirmed against Twilio docs:
///   - Send/schedule:   POST {messaging}/2010-04-01/Accounts/{sid}/Messages.json
///   - Cancel/redact:   POST {messaging}/2010-04-01/Accounts/{sid}/Messages/{Sid}.json
///   - Read one:        GET  {messaging}/2010-04-01/Accounts/{sid}/Messages/{Sid}.json
///   - List:            GET  {messaging}/2010-04-01/Accounts/{sid}/Messages.json?From=..&DateSent>=..&DateSent<=..
///   - Validate number: GET  https://lookups.twilio.com/v2/PhoneNumbers/{e164}
///
/// The shopper's number and the message body are never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private const string LookupsBase = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, TwilioSettings settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings;
        _logger = logger;
    }

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBase : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{MessagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private AuthenticationHeaderValue BasicAuth()
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    // ----------------------------------------------------------------- Validate (Lookup v2)

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber ?? string.Empty)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = BasicAuth();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        // Twilio returns 404 when the input can't be parsed as a phone number at all — treat as invalid.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Twilio Lookup rejected a number as not a phone number.");
            return PhoneNumberValidationResult.Invalid(new[] { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
            throw ErrorFrom("Twilio Lookup failed", response.StatusCode, content);

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            var errors = new List<string>();
            if (root.TryGetProperty("validation_errors", out var ve) && ve.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in ve.EnumerateArray())
                {
                    var s = e.GetString();
                    if (!string.IsNullOrEmpty(s)) errors.Add(s);
                }
            }
            if (errors.Count == 0) errors.Add("INVALID");
            _logger.LogInformation("Twilio Lookup reported a number invalid: {Reasons}.", string.Join(",", errors));
            return PhoneNumberValidationResult.Invalid(errors);
        }

        var canonical = root.TryGetProperty("phone_number", out var pn) ? pn.GetString() : null;
        if (string.IsNullOrEmpty(canonical))
            throw new TwilioApiException("Twilio Lookup returned a valid number without a canonical phone_number.");

        return PhoneNumberValidationResult.Valid(canonical);
    }

    // ----------------------------------------------------------------- Send / Schedule

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Twilio requires a Messaging Service (not a From number) to schedule a message.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = BasicAuth();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ErrorFrom("Twilio message create failed", response.StatusCode, content);

        var (sid, status, errorCode, errorMessage) = ReadMessageFields(content);
        _logger.LogInformation("Twilio accepted message {Sid} with status {Status}.", sid, status);
        return new SmsSendResult(sid, status, errorCode, errorMessage);
    }

    // ----------------------------------------------------------------- Cancel scheduled

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageUrl(providerMessageSid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = BasicAuth();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ErrorFrom("Twilio cancel scheduled message failed", response.StatusCode, content);

        _logger.LogInformation("Twilio canceled scheduled message {Sid}.", providerMessageSid);
    }

    // ----------------------------------------------------------------- Read one

    public async Task<SmsDeliveryState> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageUrl(providerMessageSid));
        request.Headers.Authorization = BasicAuth();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ErrorFrom("Twilio fetch message failed", response.StatusCode, content);

        var (_, status, errorCode, errorMessage) = ReadMessageFields(content);
        return new SmsDeliveryState(status, errorCode, errorMessage);
    }

    // ----------------------------------------------------------------- Redact body

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redaction = update the message with an empty Body. The record (sid/status) survives; the text does not.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageUrl(providerMessageSid))
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = BasicAuth();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw ErrorFrom("Twilio redact message failed", response.StatusCode, content);

        _logger.LogInformation("Twilio redacted body of message {Sid}.", providerMessageSid);
    }

    // ----------------------------------------------------------------- List for reconciliation

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Sender filter is applied by the provider (not after the fact). Date filter is day-granular (GMT)
        // per the API; we bound the query by day and refine to the exact [from,to] window client-side below.
        var fromDay = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&DateSent%3E=").Append(fromDay);   // DateSent>=
        query.Append("&DateSent%3C=").Append(toDay);     // DateSent<=
        query.Append("&PageSize=1000");

        var results = new List<ProviderMessageRecord>();
        string? nextUrl = $"{MessagesUrl}?{query}";
        var safetyPages = 0;

        while (nextUrl != null && safetyPages++ < 1000)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            request.Headers.Authorization = BasicAuth();

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw ErrorFrom("Twilio list messages failed", response.StatusCode, content);

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = GetString(m, "sid");
                    if (string.IsNullOrEmpty(sid)) continue;

                    var dateSent = ParseTwilioDate(GetString(m, "date_sent"));

                    // Refine to the exact requested window (the day-granular query is a superset).
                    if (dateSent.HasValue && (dateSent.Value < from || dateSent.Value > to))
                        continue;

                    results.Add(new ProviderMessageRecord(
                        sid!,
                        GetString(m, "status"),
                        GetString(m, "to"),
                        GetString(m, "from"),
                        dateSent,
                        GetInt(m, "error_code"),
                        GetString(m, "error_message")));
                }
            }

            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : $"{MessagingBase}{next}";
        }

        _logger.LogInformation("Twilio reconciliation listed {Count} messages for the configured sender in range.", results.Count);
        return results;
    }

    // ----------------------------------------------------------------- helpers

    private static (string? sid, string? status, int? errorCode, string? errorMessage) ReadMessageFields(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return (GetString(root, "sid"), GetString(root, "status"), GetInt(root, "error_code"), GetString(root, "error_message"));
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var p)) return null;
        return p.ValueKind switch
        {
            JsonValueKind.Number when p.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }

    private TwilioApiException ErrorFrom(string context, HttpStatusCode statusCode, string content)
    {
        int? code = null;
        string? providerMessage = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            code = GetInt(doc.RootElement, "code");
            providerMessage = GetString(doc.RootElement, "message");
        }
        catch (JsonException)
        {
            // Non-JSON error body; ignore.
        }

        _logger.LogWarning("{Context}: HTTP {Status}, provider code {Code}.", context, (int)statusCode, code);
        return new TwilioApiException($"{context} (HTTP {(int)statusCode}).", (int)statusCode, code, providerMessage);
    }
}
