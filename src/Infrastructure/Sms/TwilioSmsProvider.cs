using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Talks to Twilio's REST API over HTTP exactly as its documentation describes: HTTP Basic auth
/// (Account SID / Auth Token), form-encoded request bodies, JSON responses. It is the only place that
/// knows the wire protocol. It never logs the shopper's number, the message body, or the auth token.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // The classic messaging/core API and the lookup API are served from different hosts. Only the
    // messaging host is overridable via Twilio:BaseUrl; lookup is always the provider's own host.
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private static readonly string LookupBaseUrl =
        System.Environment.GetEnvironmentVariable("Twilio__LookupsBaseUrl") is { Length: > 0 } o
            ? o
            : "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) ||
            string.IsNullOrWhiteSpace(_settings.AuthToken) ||
            string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new SmsProviderException("Twilio is not configured: AccountSid, AuthToken and FromNumber are required.");
        }

        _messagingBaseUrl = (string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!).TrimEnd('/');

        // HTTP Basic auth: Account SID as username, Auth Token as password. Set once as a default
        // header so the token never appears in a URL or a log line.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", basic);
    }

    private string MessagesCollectionUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageResourceUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 tells us whether the number is a usable destination and gives its canonical E.164 form.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider does not recognise the number at all.
            return PhoneNumberValidationResult.Invalid("NOT_FOUND");
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.StatusCode.Equals(HttpStatusCode.OK))
        {
            var (code, _) = TryParseError(payload);
            throw new SmsProviderException($"Number lookup failed (HTTP {(int)response.StatusCode}).", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            string? reason = null;
            if (root.TryGetProperty("validation_errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                reason = errs[0].GetString();
            }
            return PhoneNumberValidationResult.Invalid(reason ?? "INVALID");
        }

        var canonical = root.TryGetProperty("phone_number", out var pn) ? pn.GetString() : null;
        if (string.IsNullOrEmpty(canonical))
        {
            // Valid but no canonical form returned; treat as not usable rather than store raw input.
            return PhoneNumberValidationResult.Invalid("NO_CANONICAL_FORM");
        }

        return PhoneNumberValidationResult.Valid(canonical);
    }

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        // Send now from the configured sending number so the message is attributed to Twilio:FromNumber
        // (which is what reconciliation later asks the provider about).
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };

        var result = await CreateMessageAsync(form, scheduledSendAt: null, cancellationToken);
        _logger.LogInformation("Twilio message created sid={Sid} status={Status}", result.MessageSid, result.Status);
        return result;
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new SmsProviderException("Scheduling requires Twilio:MessagingServiceSid, which is not configured.");
        }

        var sendAtIso = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // A scheduled message must go through a Messaging Service. Pin the sender to the configured
        // From number so the message still reconciles against Twilio:FromNumber; if the provider will
        // not accept a pinned sender on this service, fall back to letting the service choose one.
        var pinned = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtIso
        };

        try
        {
            var result = await CreateMessageAsync(pinned, sendAt, cancellationToken);
            _logger.LogInformation("Twilio message scheduled sid={Sid} status={Status} sendAt={SendAt}", result.MessageSid, result.Status, sendAtIso);
            return result;
        }
        catch (SmsProviderException ex)
        {
            _logger.LogWarning("Scheduling with a pinned sender failed (code {Code}); retrying via the messaging service pool.", ex.ProviderErrorCode);
            var pooled = new Dictionary<string, string>
            {
                ["To"] = toNumber,
                ["MessagingServiceSid"] = _settings.MessagingServiceSid,
                ["Body"] = body,
                ["ScheduleType"] = "fixed",
                ["SendAt"] = sendAtIso
            };
            var result = await CreateMessageAsync(pooled, sendAt, cancellationToken);
            _logger.LogInformation("Twilio message scheduled sid={Sid} status={Status} sendAt={SendAt}", result.MessageSid, result.Status, sendAtIso);
            return result;
        }
    }

    private async Task<SmsSendResult> CreateMessageAsync(IDictionary<string, string> form, DateTimeOffset? scheduledSendAt, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesCollectionUrl, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Do not surface the provider's raw message: on a bad request it can echo the destination
            // number. The stable, non-PII error code is enough to act on.
            var (code, _) = TryParseError(payload);
            throw new SmsProviderException($"The provider rejected the message (HTTP {(int)response.StatusCode}).", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = GetString(root, "sid");
        var status = GetString(root, "status");
        if (string.IsNullOrEmpty(sid) || string.IsNullOrEmpty(status))
        {
            throw new SmsProviderException("The provider accepted the message but returned no identifier or status.");
        }

        return new SmsSendResult
        {
            MessageSid = sid!,
            Status = status!,
            ErrorCode = GetInt(root, "error_code"),
            ErrorMessage = GetString(root, "error_message"),
            ScheduledSendAt = scheduledSendAt
        };
    }

    public async Task<SmsStatusResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageResourceUrl(messageSid), cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, _) = TryParseError(payload);
            throw new SmsProviderException($"Reading the message failed (HTTP {(int)response.StatusCode}).", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsStatusResult
        {
            Status = GetString(root, "status") ?? string.Empty,
            ErrorCode = GetInt(root, "error_code"),
            // error_message on the Message resource describes the error code; it does not carry the number.
            ErrorMessage = GetString(root, "error_message")
        };
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageResourceUrl(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = TryParseError(payload);
            throw new SmsProviderException($"Cancelling the scheduled message failed (HTTP {(int)response.StatusCode}).", code);
        }
        _logger.LogInformation("Twilio scheduled message cancelled sid={Sid}", messageSid);
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Redacting the body means updating the message with an empty Body. The provider keeps the
        // record (and its delivery outcome) but the text is no longer retrievable there.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageResourceUrl(messageSid), content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = TryParseError(payload);
            throw new SmsProviderException($"Disposing of the message content failed (HTTP {(int)response.StatusCode}).", code);
        }
        _logger.LogInformation("Twilio message body redacted sid={Sid}", messageSid);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromIso = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Ask the provider for THIS sending number's messages in the range (the From filter is applied
        // by the provider, not by us filtering a wider answer afterwards). DateSent> / DateSent< carry
        // the range bounds; %3E / %3C are the encoded '>' / '<' that name those parameters.
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&PageSize=1000" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromIso)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toIso)}";

        var authority = new Uri(_messagingBaseUrl).GetLeftPart(UriPartial.Authority);
        var url = MessagesCollectionUrl + query;

        var records = new List<ProviderMessageRecord>();
        var safetyPageCap = 1000;

        while (!string.IsNullOrEmpty(url) && safetyPageCap-- > 0)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, _) = TryParseError(payload);
                throw new SmsProviderException($"Listing provider messages failed (HTTP {(int)response.StatusCode}).", code);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = GetString(m, "sid");
                    if (string.IsNullOrEmpty(sid)) continue;
                    records.Add(new ProviderMessageRecord
                    {
                        MessageSid = sid!,
                        Status = GetString(m, "status"),
                        From = GetString(m, "from"),
                        DateSent = ParseProviderDate(GetString(m, "date_sent")),
                        DateCreated = ParseProviderDate(GetString(m, "date_created")),
                        ErrorCode = GetInt(m, "error_code")
                    });
                }
            }

            // The classic API returns a relative next_page_uri; resolve it against the messaging host.
            var next = root.TryGetProperty("next_page_uri", out var np) && np.ValueKind == JsonValueKind.String
                ? np.GetString()
                : null;
            url = string.IsNullOrEmpty(next) ? null! : authority + next;
        }

        _logger.LogInformation("Reconciliation listed {Count} provider messages for the configured sender.", records.Count);
        return records;
    }

    private static (int? code, string? message) TryParseError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return (null, null);
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var code = GetInt(root, "code");
            var message = GetString(root, "message");
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Provider timestamps are RFC 2822 (e.g. "Fri, 24 May 2019 17:44:46 +0000").
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
