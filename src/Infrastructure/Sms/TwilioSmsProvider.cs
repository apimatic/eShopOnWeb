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

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to the SMS provider's REST API over HTTP: the classic messaging API (send / read / list /
/// update) and the lookup API (number validation). This is the only place that speaks the provider's
/// wire format. Every messaging call honours <see cref="TwilioSettings.BaseUrl"/> when set; lookup is a
/// different host and is not governed by that setting.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // Provider default hosts. The messaging host is overridable via configuration; the lookup host is not.
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ClassicApiVersion = "2010-04-01";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public string FromNumber => _settings.FromNumber;

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}/{ClassicApiVersion}/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageResourceUrl(string sid) =>
        $"{MessagingBaseUrl}/{ClassicApiVersion}/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    // ---------------------------------------------------------------- Lookup / validation

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup API: GET /v2/PhoneNumbers/{PhoneNumber}. Returns the canonical E.164 form plus a
        // `valid` flag and any validation_errors. A number the API cannot even parse comes back 404.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsProviderException($"Lookup failed with status {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    errors.Add(e.GetString()!);
                }
            }
        }

        // A usable destination is one the provider reports as valid and for which it produced a canonical form.
        var isValid = valid && !string.IsNullOrEmpty(canonical);
        return new PhoneNumberValidationResult(isValid, canonical, errors);
    }

    // ---------------------------------------------------------------- Sending

    public async Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body),
        };
        var message = await PostMessageAsync(MessagesCollectionUrl, form, cancellationToken);
        _logger.LogInformation("SMS provider accepted message {Sid} with status {Status}.", message.Sid, message.Status ?? "unknown");
        return message;
    }

    public async Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
        // The provider — not this application — holds the message until SendAt.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
        };
        var message = await PostMessageAsync(MessagesCollectionUrl, form, cancellationToken);
        _logger.LogInformation("SMS provider queued scheduled message {Sid} with status {Status}.", message.Sid, message.Status ?? "unknown");
        return message;
    }

    // ---------------------------------------------------------------- Reads / updates

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageResourceUrl(messageSid), cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new SmsProviderException($"Fetching message failed with status {(int)response.StatusCode}.", (int)response.StatusCode);
        }
        using var doc = JsonDocument.Parse(content);
        return ParseMessage(doc.RootElement);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Cancel a not-yet-sent message: POST the resource with Status=canceled (the only accepted value).
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        var message = await PostMessageAsync(MessageResourceUrl(messageSid), form, cancellationToken);
        _logger.LogInformation("SMS provider cancelled scheduled message {Sid}; status now {Status}.", message.Sid, message.Status ?? "unknown");
        return message;
    }

    public async Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redact the body at the provider: POST the resource with an empty Body. The record survives,
        // but the text is no longer retrievable from the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        var message = await PostMessageAsync(MessageResourceUrl(messageSid), form, cancellationToken);
        _logger.LogInformation("SMS provider redacted body of message {Sid}.", message.Sid);
        return message;
    }

    // ---------------------------------------------------------------- Reconciliation list

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for messages sent FROM our configured number (a sender-side filter),
        // over a date window widened by a day at each edge so the whole requested range is covered even
        // though the provider's DateSent filter is date-granular. Precise [from,to] filtering happens below.
        var fromDate = from.ToUniversalTime().Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = new StringBuilder();
        query.Append("From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append('&').Append(Uri.EscapeDataString("DateSent>")).Append('=').Append(Uri.EscapeDataString(fromDate));
        query.Append('&').Append(Uri.EscapeDataString("DateSent<")).Append('=').Append(Uri.EscapeDataString(toDate));
        query.Append("&PageSize=1000");

        var results = new List<ProviderMessage>();
        var nextUrl = $"{MessagesCollectionUrl}?{query}";

        // Follow the provider's paging until it stops handing back a next page, so the report covers the whole range.
        var safetyPageLimit = 1000;
        while (!string.IsNullOrEmpty(nextUrl) && safetyPageLimit-- > 0)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new SmsProviderException($"Listing messages failed with status {(int)response.StatusCode}.", (int)response.StatusCode);
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messagesEl) && messagesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messagesEl.EnumerateArray())
                {
                    results.Add(ParseMessage(m));
                }
            }

            var nextPageUri = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(nextPageUri) ? null! : CombineWithMessagingHost(nextPageUri);
        }

        // Precise range enforcement: keep messages whose send time (or, if not yet sent, creation time)
        // falls within the requested window.
        var precise = new List<ProviderMessage>();
        foreach (var m in results)
        {
            var stamp = m.DateSent ?? m.DateCreated;
            if (stamp == null || (stamp.Value >= from && stamp.Value <= to))
            {
                precise.Add(m);
            }
        }
        return precise;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<ProviderMessage> PostMessageAsync(string url, IEnumerable<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            int? providerCode = TryReadProviderErrorCode(content);
            throw new SmsProviderException(
                $"Provider request failed with status {(int)response.StatusCode}.",
                (int)response.StatusCode,
                providerCode);
        }

        using var doc = JsonDocument.Parse(content);
        return ParseMessage(doc.RootElement);
    }

    private string CombineWithMessagingHost(string relativeOrAbsolute)
    {
        if (relativeOrAbsolute.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return relativeOrAbsolute;
        }
        return $"{MessagingBaseUrl}/{relativeOrAbsolute.TrimStart('/')}";
    }

    private static int? TryReadProviderErrorCode(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
            {
                return codeEl.GetInt32();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; nothing to extract.
        }
        return null;
    }

    private static ProviderMessage ParseMessage(JsonElement el)
    {
        var sid = GetString(el, "sid") ?? string.Empty;
        var status = GetString(el, "status");
        var errorCode = GetNullableInt(el, "error_code");
        var errorMessage = GetString(el, "error_message");
        var to = GetString(el, "to");
        var from = GetString(el, "from");
        var dateSent = GetNullableDate(el, "date_sent");
        var dateCreated = GetNullableDate(el, "date_created");
        return new ProviderMessage(sid, status, errorCode, errorMessage, to, from, dateSent, dateCreated);
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetNullableInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }
        return p.ValueKind switch
        {
            JsonValueKind.Number => p.GetInt32(),
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) => v,
            _ => null
        };
    }

    private static DateTimeOffset? GetNullableDate(JsonElement el, string name)
    {
        var raw = GetString(el, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        // The provider returns RFC-2822 timestamps in GMT (e.g. "Mon, 12 Aug 2026 12:00:00 +0000").
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto.ToUniversalTime();
        }
        return null;
    }
}
