using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/>. Every Twilio interaction this integration
/// performs is a documented Twilio REST call issued from here:
/// <list type="bullet">
/// <item>Send / schedule — <c>POST /2010-04-01/Accounts/{Sid}/Messages.json</c></item>
/// <item>Fetch — <c>GET  /2010-04-01/Accounts/{Sid}/Messages/{Sid}.json</c></item>
/// <item>Cancel scheduled / redact body — <c>POST /2010-04-01/Accounts/{Sid}/Messages/{Sid}.json</c></item>
/// <item>Reconcile — <c>GET  /2010-04-01/Accounts/{Sid}/Messages.json?From=...&amp;DateSent...</c></item>
/// <item>Validate a number — Lookup v2 <c>GET /v2/PhoneNumbers/{E164}</c></item>
/// </list>
/// The messaging calls honour <see cref="TwilioSettings.BaseUrl"/> when set; Lookup is always served
/// from its own host. Phone numbers and the auth token are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient http, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        // HTTP Basic: Account SID as username, Auth Token as password. Built once, never logged.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 is served from its own host and is not governed by the messaging BaseUrl override.
        // Basic Lookup (no Fields) is free and returns the validation verdict and canonical E.164 form.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A malformed number produces a routing error here; treat it as "not a usable destination".
            _logger.LogWarning("Lookup returned HTTP {0} when validating a number.", (int)response.StatusCode);
            return PhoneNumberValidationResult.Invalid("The number could not be validated by the provider.");
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            var reason = "The provider does not consider this a valid, reachable number.";
            if (root.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            {
                var parts = new List<string>();
                foreach (var e in errors.EnumerateArray())
                {
                    if (e.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(e.GetString()!);
                    }
                }
                if (parts.Count > 0)
                {
                    reason = $"The provider rejected the number: {string.Join(", ", parts)}.";
                }
            }
            return PhoneNumberValidationResult.Invalid(reason);
        }

        var canonical = root.TryGetProperty("phone_number", out var pn) && pn.ValueKind == JsonValueKind.String
            ? pn.GetString()
            : null;

        return string.IsNullOrEmpty(canonical)
            ? PhoneNumberValidationResult.Invalid("The provider did not return a canonical form for the number.")
            : PhoneNumberValidationResult.Valid(canonical!);
    }

    public Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        // Immediate send from this application's own configured number, so reconciliation by From matches.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service feature: ScheduleType=fixed + SendAt + MessagingServiceSid.
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Status=canceled is the only value this parameter accepts; it calls off a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = ExtractProviderError(payload, (int)response.StatusCode);
            _logger.LogWarning("Cancel of scheduled message {0} returned HTTP {1}.", messageSid, (int)response.StatusCode);
            return SmsSendResult.Failed(message);
        }

        return ParseMessageResource(payload);
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        using var response = await _http.GetAsync(url, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Fetch of message {0} returned HTTP {1}.", messageSid, (int)response.StatusCode);
            return null;
        }

        using var doc = JsonDocument.Parse(payload);
        return ToProviderMessage(doc.RootElement);
    }

    public async Task<bool> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redaction: POST the message with an empty Body. The provider drops the text; the record and
        // its delivery outcome survive, so the body is no longer retrievable at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Content disposal for message {0} returned HTTP {1}.", messageSid, (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own number's messages directly (server-side From
        // filter), constrained to the date range, and follow every page so the whole range is covered.
        var fromDate = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var next =
            $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json" +
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={fromDate}&DateSent%3C={toDate}&PageSize=1000";

        var results = new List<ProviderMessage>();
        var safetyPageLimit = 100;

        while (next is not null && safetyPageLimit-- > 0)
        {
            var url = next.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? next : $"{_messagingBaseUrl}{next}";
            using var response = await _http.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Reconciliation list returned HTTP {0}.", (int)response.StatusCode);
                break;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var projected = ToProviderMessage(message);
                    // Refine to the exact [from, to] window (the API filter is date-granular).
                    if (projected.DateSent is { } sent && (sent < from || sent > to))
                    {
                        continue;
                    }
                    results.Add(projected);
                }
            }

            next = root.TryGetProperty("next_page_uri", out var np) && np.ValueKind == JsonValueKind.String
                ? np.GetString()
                : null;
        }

        return results;
    }

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var response = await _http.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider refused to create the message (bad credentials, unusable sender, etc.).
            // This is a submission failure, reported as a result — the caller's operation still succeeds.
            var message = ExtractProviderError(payload, (int)response.StatusCode);
            _logger.LogWarning("Create message returned HTTP {0}.", (int)response.StatusCode);
            return SmsSendResult.Failed(message);
        }

        return ParseMessageResource(payload);
    }

    private static SmsSendResult ParseMessageResource(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = GetString(root, "sid");
        var status = GetString(root, "status");
        var (errorCode, errorMessage) = GetError(root);
        return new SmsSendResult(true, sid, status, errorCode, errorMessage);
    }

    private static ProviderMessage ToProviderMessage(JsonElement root)
    {
        var sid = GetString(root, "sid") ?? string.Empty;
        var status = GetString(root, "status");
        var (errorCode, errorMessage) = GetError(root);
        DateTimeOffset? dateSent = null;
        var raw = GetString(root, "date_sent");
        if (!string.IsNullOrEmpty(raw) && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }
        return new ProviderMessage(sid, status, errorCode, errorMessage, dateSent, GetString(root, "to"));
    }

    private static (int? code, string? message) GetError(JsonElement root)
    {
        int? code = null;
        if (root.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number)
        {
            code = ec.GetInt32();
        }
        var message = GetString(root, "error_message");
        return (code, message);
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static string ExtractProviderError(string payload, int statusCode)
    {
        // Twilio error bodies are JSON: { "code": 21211, "message": "...", "status": 400, ... }.
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var message = GetString(doc.RootElement, "message");
            if (!string.IsNullOrEmpty(message))
            {
                return message!;
            }
        }
        catch (JsonException)
        {
            // Non-JSON body; fall through to a generic message that leaks nothing sensitive.
        }
        return $"The provider rejected the request (HTTP {statusCode}).";
    }
}
