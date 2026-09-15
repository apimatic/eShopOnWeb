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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// <see cref="ISmsProvider"/> over Twilio's REST API. All wire details live here: hosts, HTTP Basic auth,
/// form encoding and JSON parsing, per the twilio-docs reference.
///
/// PII discipline: this type never logs phone numbers, message bodies, or credentials. The typed
/// <see cref="HttpClient"/> is registered with its default loggers removed so that request URLs — which
/// carry the looked-up number in the path and the sender/recipient in query strings — are never written
/// to logs either. Provider error responses (whose text can echo a number) are turned into a sanitized
/// <see cref="SmsProviderException"/> carrying only the HTTP status and Twilio error code.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // The Lookup API is served from its own host and is NOT governed by the messaging BaseUrl override.
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;

        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public string ConfiguredSenderNumber => _settings.FromNumber;

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageUrl(string sid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2, basic (free) validation. The leading '+' must be percent-encoded in the path.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        // A number the provider cannot even parse comes back 400/404 — treat as an unusable destination
        // rather than an infrastructure error. (A number that merely fails validation returns 200.)
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var valid = root.TryGetProperty("valid", out var validElement) && validElement.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in errorsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    errors.Add(item.GetString()!);
                }
            }
        }

        return new PhoneNumberLookupResult(valid && !string.IsNullOrEmpty(canonical), canonical, errors);
    }

    public Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; pinning From keeps the sender our configured number.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsMessageState?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(payload);
        return ReadMessageState(document.RootElement);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redacting the body at the provider = update the message with an empty Body.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageUrl(providerMessageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await CreateProviderExceptionAsync(response, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for OUR sender's messages over the range. Date filters are day-granular and the
        // operator is part of the (percent-encoded) parameter name. Their semantics are asymmetric:
        // "DateSent>" (%3E) with value D is inclusive of day D, while "DateSent<" (%3C) with value D is
        // exclusive (it means "before the start of D"). So the lower bound is the from-day and the upper
        // bound is the day AFTER the to-day; the exact [from, to] window is then trimmed in-app by the
        // caller. This keeps the query bounded to the range while covering the whole of it.
        var fromDate = from.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDateExclusive = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}&DateSent%3E={fromDate}&DateSent%3C={toDateExclusive}&PageSize=1000";

        var results = new List<SmsMessageState>();
        string? url = MessagesUrl + query;
        var pageGuard = 0;

        while (url is not null && pageGuard++ < 1000)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw await CreateProviderExceptionAsync(response, cancellationToken);
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var state = ReadMessageState(message);
                    if (state is not null)
                    {
                        results.Add(state);
                    }
                }
            }

            // Follow paging. next_page_uri is relative to the default host; re-issue its query against our
            // configured messaging base so a BaseUrl override is honored on every page.
            var next = GetString(root, "next_page_uri");
            if (string.IsNullOrEmpty(next))
            {
                break;
            }
            var queryStart = next.IndexOf('?');
            url = queryStart >= 0 ? MessagesUrl + next.Substring(queryStart) : null;
        }

        return results;
    }

    private async Task<SmsDispatchResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesUrl, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw CreateProviderException((int)response.StatusCode, payload);
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        var sid = GetString(root, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            throw new SmsProviderException((int)response.StatusCode, null, "Messaging provider did not return a message identifier.");
        }

        var status = GetString(root, "status") ?? MessageDeliveryStatus.Queued;
        var errorCode = GetInt(root, "error_code");
        return new SmsDispatchResult(sid, status, errorCode);
    }

    private static SmsMessageState? ReadMessageState(JsonElement element)
    {
        var sid = GetString(element, "sid");
        if (string.IsNullOrEmpty(sid))
        {
            return null;
        }

        return new SmsMessageState(
            sid,
            GetString(element, "status") ?? string.Empty,
            GetInt(element, "error_code"),
            GetString(element, "to"),
            GetString(element, "from"),
            ParseTwilioDate(GetString(element, "date_sent")));
    }

    private async Task<SmsProviderException> CreateProviderExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        return CreateProviderException((int)response.StatusCode, payload);
    }

    private SmsProviderException CreateProviderException(int httpStatus, string payload)
    {
        int? twilioCode = null;
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
                && codeElement.TryGetInt32(out var code))
            {
                twilioCode = code;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — ignore; we never surface raw provider text (it can contain PII).
        }

        // Sanitized on purpose: HTTP status + Twilio error code only, safe to log.
        var message = twilioCode is not null
            ? $"Messaging provider returned HTTP {httpStatus} (Twilio error {twilioCode})."
            : $"Messaging provider returned HTTP {httpStatus}.";
        return new SmsProviderException(httpStatus, twilioCode, message);
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? GetInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var number))
        {
            return number;
        }
        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Messaging (/2010-04-01) responses use RFC 2822, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        var formats = new[]
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss K",
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'"
        };
        if (DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var exact))
        {
            return exact;
        }
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var general))
        {
            return general;
        }
        return null;
    }
}
