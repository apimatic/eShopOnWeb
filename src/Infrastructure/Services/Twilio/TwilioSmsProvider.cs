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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to the messaging provider over its documented HTTP API. The classic messaging API
/// (create/fetch/list/redact/cancel a Message) is reached through <see cref="TwilioOptions.BaseUrl"/>
/// when that override is set, and through the provider's default host otherwise. Number lookup is a
/// separate host and is deliberately not governed by that override.
///
/// Destination numbers and message bodies are never written to logs; the auth token never leaves this
/// class except as an HTTP Basic credential.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private const string LookupBase = "https://lookups.twilio.com";
    private const int MaxPages = 200;

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredSenderNumber => _options.FromNumber;

    private string MessagingBase =>
        string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultMessagingBase : _options.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBase}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{MessagingBase}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private AuthenticationHeaderValue BasicAuth()
    {
        var raw = Encoding.UTF8.GetBytes($"{_options.AccountSid}:{_options.AuthToken}");
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        var request = new HttpRequestMessage(method, url) { Headers = { Authorization = BasicAuth() } };
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }
        return request;
    }

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        // Free validation + canonicalisation: no Fields requested. Lookup lives on its own host, which
        // the messaging BaseUrl override must not touch.
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var request = NewRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
            var phoneNumber = GetString(root, "phone_number");
            var nationalFormat = GetString(root, "national_format");
            var errors = ReadValidationErrors(root);
            return new PhoneLookupResult(valid, phoneNumber, nationalFormat, errors);
        }

        // v2 reports an unusable number as 200 valid:false; a 400/404 here means the input could not be
        // validated at all. Treat that as "not a usable destination" rather than a transport failure.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Number lookup rejected input with status {Status}.", (int)response.StatusCode);
            return new PhoneLookupResult(false, null, null, new[] { "PROVIDER_REJECTED_INPUT" });
        }

        throw new HttpRequestException($"Number lookup failed with status {(int)response.StatusCode}.");
    }

    public async Task<SmsSendResult> SendAsync(SmsSendCommand command, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", command.To),
            new("Body", command.Body)
        };

        if (command.SendAt is null)
        {
            // Immediate: send from the configured number so the message is attributable to it.
            form.Add(new("From", _options.FromNumber));
        }
        else
        {
            // Scheduling requires a Messaging Service; pin the sender to the configured number so the
            // scheduled message is still attributable to it for reconciliation.
            form.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
            form.Add(new("From", _options.FromNumber));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", command.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var request = NewRequest(HttpMethod.Post, MessagesUrl, form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var sid = GetString(root, "sid");
            var status = GetString(root, "status");
            var (errorCode, errorMessage) = ReadError(root);
            var dateSent = ParseDate(GetString(root, "date_sent"));
            _logger.LogInformation("Provider accepted message {Sid} with status {Status} (scheduled={Scheduled}).",
                sid, status, command.SendAt is not null);
            return new SmsSendResult(true, sid, status, errorCode, errorMessage, dateSent);
        }

        var (code, message) = ReadProviderError(payload);
        _logger.LogWarning("Provider rejected create-message with HTTP {Status}, code {Code}.", (int)response.StatusCode, code);
        return new SmsSendResult(false, null, null, code, message ?? $"HTTP {(int)response.StatusCode}");
    }

    public async Task<SmsMessageState?> FetchAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, MessageUrl(providerMessageId));
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Fetch message failed with status {(int)response.StatusCode}.");
        }

        using var doc = JsonDocument.Parse(payload);
        return ReadMessageState(doc.RootElement);
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListSentFromAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this number's messages directly (From filter), not a wider answer we then
        // trim. The provider's DateSent filter is date-granular, so widen the window by a day on each side
        // and let the caller filter to the precise instant range.
        var lower = from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var upper = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = $"?From={Uri.EscapeDataString(fromNumber)}&DateSent%3E={lower}&DateSent%3C={upper}&PageSize=1000";
        var url = MessagesUrl + query;

        var results = new List<SmsMessageState>();
        var pages = 0;

        while (!string.IsNullOrEmpty(url) && pages < MaxPages)
        {
            pages++;
            using var request = NewRequest(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"List messages failed with status {(int)response.StatusCode}.");
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    results.Add(ReadMessageState(message));
                }
            }

            var next = GetString(root, "next_page_uri");
            url = string.IsNullOrEmpty(next) ? null : MessagingBase + next;
        }

        _logger.LogInformation("Reconciliation read {Count} provider message(s) across {Pages} page(s).", results.Count, pages);
        return results;
    }

    public async Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        // Redacting the body (empty Body) removes the text at the provider while the message resource,
        // and therefore its delivery outcome, survives.
        var form = new[] { new KeyValuePair<string, string>("Body", string.Empty) };
        using var request = NewRequest(HttpMethod.Post, MessageUrl(providerMessageId), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = ReadProviderError(payload);
            throw new HttpRequestException($"Redact message failed with status {(int)response.StatusCode}, code {code}.");
        }
        _logger.LogInformation("Redacted body of message {Sid} at provider.", providerMessageId);
    }

    public async Task<bool> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        var form = new[] { new KeyValuePair<string, string>("Status", "canceled") };
        using var request = NewRequest(HttpMethod.Post, MessageUrl(providerMessageId), form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider refuses to cancel a message that has already left the scheduled state.
            var (code, _) = ReadProviderError(payload);
            _logger.LogWarning("Could not cancel scheduled message {Sid}: HTTP {Status}, code {Code}.",
                providerMessageId, (int)response.StatusCode, code);
            return false;
        }

        using var doc = JsonDocument.Parse(payload);
        var status = GetString(doc.RootElement, "status");
        var canceled = string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase);
        _logger.LogInformation("Cancel request for scheduled message {Sid} left it in status {Status}.", providerMessageId, status);
        return canceled;
    }

    private static SmsMessageState ReadMessageState(JsonElement el)
    {
        var (errorCode, errorMessage) = ReadError(el);
        return new SmsMessageState(
            Sid: GetString(el, "sid") ?? string.Empty,
            To: GetString(el, "to"),
            From: GetString(el, "from"),
            Status: GetString(el, "status"),
            ErrorCode: errorCode,
            ErrorMessage: errorMessage,
            DateSent: ParseDate(GetString(el, "date_sent")),
            Body: GetString(el, "body"));
    }

    private static IReadOnlyList<string> ReadValidationErrors(JsonElement root)
    {
        if (root.TryGetProperty("validation_errors", out var el) && el.ValueKind == JsonValueKind.Array)
        {
            return el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }
        return Array.Empty<string>();
    }

    private static (int? code, string? message) ReadError(JsonElement el)
    {
        int? code = null;
        if (el.TryGetProperty("error_code", out var codeEl))
        {
            if (codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var c)) code = c;
            else if (codeEl.ValueKind == JsonValueKind.String && int.TryParse(codeEl.GetString(), out var cs)) code = cs;
        }
        var message = GetString(el, "error_message");
        return (code, message);
    }

    private static (int? code, string? message) ReadProviderError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            int? code = null;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var c))
            {
                code = c;
            }
            return (code, GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement el, string name)
    {
        if (el.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            var value = prop.GetString();
            return string.IsNullOrEmpty(value) ? null : value;
        }
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
