using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Talks to Twilio's REST API over plain HTTP (no SDK), covering everything this
/// integration needs: number lookup, immediate and scheduled sends, single-message
/// fetch, scheduled-message cancel, body redaction, and listing a date range of
/// messages sent from the configured number for reconciliation.
///
/// Confirmed against Twilio docs:
///  - Messages:  POST/GET https://api.twilio.com/2010-04-01/Accounts/{Sid}/Messages[.json]
///  - Scheduling: same create endpoint with MessagingServiceSid + ScheduleType=fixed + SendAt
///  - Cancel:    POST .../Messages/{Sid}.json with Status=canceled
///  - Redact:    POST .../Messages/{Sid}.json with Body="" (empty)
///  - Lookup v2: GET https://lookups.twilio.com/v2/PhoneNumbers/{number} (different host)
/// The messaging base host is overridable via Twilio:BaseUrl; the Lookup host is not.
/// </summary>
public class TwilioMessagingClient : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersion = "2010-04-01";

    // Matches phone-number-like runs so provider error text can be scrubbed before it
    // ever reaches a log or a thrown message. A shopper's number is never logged.
    private static readonly Regex PhoneLike = new(@"\+?\d[\d\-\s().]{5,}\d", RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup lives on its own host, not governed by Twilio:BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}?Fields=line_type_intelligence";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // Twilio could not parse/validate the number: treat as an unusable destination.
            return new PhoneNumberLookupResult(false, null, null);
        }

        EnsureSuccess(response, content, "lookup a phone number");

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = GetString(root, "phone_number");
        string? lineType = null;
        if (root.TryGetProperty("line_type_intelligence", out var lti) && lti.ValueKind == JsonValueKind.Object)
        {
            lineType = GetString(lti, "type");
        }

        return new PhoneNumberLookupResult(valid, canonical, lineType);
    }

    public async Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // Immediate send uses the explicit From number so the message's `from` equals
        // Twilio:FromNumber — which is what reconciliation filters on.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesUrl(), form, "send a message", cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service (a From number is not allowed) plus
        // ScheduleType=fixed and an ISO-8601 UTC SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesUrl(), form, "schedule a message", cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var url = MessageUrl(providerMessageSid);
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content, "fetch a message");
        return ParseMessage(content);
    }

    public async Task<ProviderMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageUrl(providerMessageSid), form, "cancel a scheduled message", cancellationToken);
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redaction = update the body to empty. The record (sid, status) is preserved,
        // but the text is no longer retrievable from the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(providerMessageSid), form, "redact a message body", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();

        // Ask the provider only for this application's own sending number's messages in
        // the range, rather than filtering a wider answer after the fact.
        var from = Uri.EscapeDataString(_settings.FromNumber);
        var after = Uri.EscapeDataString(fromUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var before = Uri.EscapeDataString(toUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var nextUrl = $"{MessagesUrl()}?From={from}&DateSent%3E={after}&DateSent%3C={before}&PageSize=200";

        var safety = 0;
        while (!string.IsNullOrEmpty(nextUrl) && safety++ < 500)
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, content, "list messages for reconciliation");

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(m));
                }
            }

            var nextPage = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(nextPage) ? null : _messagingBaseUrl + nextPage;
        }

        return results;
    }

    // -- HTTP plumbing ---------------------------------------------------------

    private string MessagesUrl() => $"{_messagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/{ApiVersion}/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, string action, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, content, action);
        return ParseMessage(content);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureSuccess(HttpResponseMessage response, string content, string action)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Surface the provider's error code and (scrubbed) message. Never include the
        // auth token; scrub any phone-number-like text so it cannot leak to logs.
        string? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            code = GetString(doc.RootElement, "code");
            message = GetString(doc.RootElement, "message");
        }
        catch
        {
            // non-JSON error body; fall through with status code only
        }

        var safeMessage = Scrub(message);
        var detail = code is null ? $"HTTP {(int)response.StatusCode}" : $"code {code}";
        throw new TwilioApiException($"Twilio failed to {action} ({detail}): {safeMessage}", (int)response.StatusCode, code);
    }

    private ProviderMessage ParseMessage(string content)
    {
        using var doc = JsonDocument.Parse(content);
        return ParseMessage(doc.RootElement);
    }

    private static ProviderMessage ParseMessage(JsonElement m)
    {
        var sid = GetString(m, "sid") ?? string.Empty;
        var status = GetString(m, "status") ?? string.Empty;
        var to = GetString(m, "to");
        var from = GetString(m, "from");
        var body = GetString(m, "body");
        var errorCode = GetString(m, "error_code");
        var errorMessage = GetString(m, "error_message");
        DateTimeOffset? dateSent = null;
        var dateSentRaw = GetString(m, "date_sent");
        if (!string.IsNullOrEmpty(dateSentRaw) &&
            DateTimeOffset.TryParse(dateSentRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            dateSent = parsed;
        }

        return new ProviderMessage(sid, status, to, from, body, errorCode, errorMessage, dateSent);
    }

    /// <summary>Reads a property as a string regardless of whether it is a JSON string, number, or null.</summary>
    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => prop.GetRawText()
        };
    }

    private static string Scrub(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : PhoneLike.Replace(text, "[redacted-number]");
}

/// <summary>Raised when the Twilio API returns a non-success response.</summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(string message, int httpStatus, string? twilioCode) : base(message)
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
    }

    public int HttpStatus { get; }
    public string? TwilioCode { get; }
}
