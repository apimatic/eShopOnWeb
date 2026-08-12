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
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for Twilio's HTTP API, built to the OpenAPI specifications in
/// <c>api-specs/twilio</c>. It talks to two hosts as the specs dictate:
///  - Lookups v2 (<c>lookups.twilio.com</c>) for number validation / canonicalization, and
///  - the 2010-04-01 messaging API (<c>api.twilio.com</c>, overridable by <c>Twilio:BaseUrl</c>)
///    for creating, reading, updating (cancel/redact) and listing messages.
/// Auth on every request is HTTP Basic (AccountSid:AuthToken), the scheme the specs declare.
/// The auth token is never logged; recipient numbers are never logged.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    // Redacts phone-number-like digit runs (>= 7 digits) from any provider text before it can be
    // surfaced in an exception message / log. Five-digit Twilio error codes are left intact.
    private static readonly Regex PhoneLikeRegex = new(@"\+?\d[\d()\-\s]{5,}\d", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient httpClient, TwilioSettings settings)
    {
        _httpClient = httpClient;
        _settings = settings;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : settings.BaseUrl!.TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<PhoneNumberLookupResult> LookupPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        // A number the provider cannot even parse comes back as 404 with valid=false, which for our
        // purposes is simply "not a usable destination" rather than an error.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberLookupResult(false, null, null, null);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, payload);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var e164 = GetString(root, "phone_number");
        var national = GetString(root, "national_format");
        var country = GetString(root, "country_code");
        return new PhoneNumberLookupResult(valid, e164, national, country);
    }

    public Task<TwilioMessageResource> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<TwilioMessageResource> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service and ScheduleType=fixed with an ISO-8601 SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<TwilioMessageResource> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        // POST {base}/2010-04-01/Accounts/{AccountSid}/Messages.json
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, payload);
        }
        return ParseMessage(payload);
    }

    public async Task<TwilioMessageResource> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // GET {base}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, payload);
        }
        return ParseMessage(payload);
    }

    public Task<TwilioMessageResource> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<TwilioMessageResource> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    private async Task<TwilioMessageResource> UpdateMessageAsync(string messageSid, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        // POST {base}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw BuildApiException(response.StatusCode, payload);
        }
        return ParseMessage(payload);
    }

    public async Task<IReadOnlyList<TwilioMessageResource>> ListMessagesFromNumberAsync(
        string fromE164, DateTimeOffset dateSentFrom, DateTimeOffset dateSentTo, CancellationToken cancellationToken = default)
    {
        // GET {base}/2010-04-01/Accounts/{AccountSid}/Messages.json?From=&DateSent>=&DateSent<=
        // The DateSent> / DateSent< filters are inclusive at day granularity (per the spec); a precise
        // [from, to] datetime filter is applied client-side afterwards. The sender filter is asked of
        // the provider so only this application's own number's traffic is counted.
        var fromDay = dateSentFrom.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = dateSentTo.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query =
            $"From={Uri.EscapeDataString(fromE164)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromDay)}" +   // %3E = '>'
            $"&DateSent%3C={Uri.EscapeDataString(toDay)}" +     // %3C = '<'
            $"&PageSize=1000";
        var nextUrl = $"{_messagingBaseUrl}/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json?{query}";

        var results = new List<TwilioMessageResource>();
        var safety = 0;
        while (!string.IsNullOrEmpty(nextUrl) && safety++ < 1000)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw BuildApiException(response.StatusCode, payload);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(ParseMessage(m));
                }
            }

            // Follow pagination via next_page_uri (a path relative to the messaging host).
            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : CombineWithBase(next!);
        }

        // Precise range bound: keep messages sent within [from, to]; retain not-yet-dated messages the
        // range query returned (they have no date_sent yet).
        var filtered = new List<TwilioMessageResource>(results.Count);
        foreach (var m in results)
        {
            if (m.DateSent is null || (m.DateSent >= dateSentFrom && m.DateSent <= dateSentTo))
            {
                filtered.Add(m);
            }
        }
        return filtered;
    }

    private string CombineWithBase(string relativeOrAbsolute)
    {
        if (relativeOrAbsolute.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            relativeOrAbsolute.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return relativeOrAbsolute;
        }
        return $"{_messagingBaseUrl}/{relativeOrAbsolute.TrimStart('/')}";
    }

    // ---- parsing -------------------------------------------------------------------------------

    private static TwilioMessageResource ParseMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ParseMessage(doc.RootElement);
    }

    private static TwilioMessageResource ParseMessage(JsonElement root)
    {
        return new TwilioMessageResource(
            Sid: GetString(root, "sid"),
            Status: GetString(root, "status"),
            To: GetString(root, "to"),
            From: GetString(root, "from"),
            Body: GetString(root, "body"),
            ErrorCode: GetInt(root, "error_code"),
            ErrorMessage: GetString(root, "error_message"),
            MessagingServiceSid: GetString(root, "messaging_service_sid"),
            DateSent: GetDate(root, "date_sent"),
            DateCreated: GetDate(root, "date_created"),
            DateUpdated: GetDate(root, "date_updated"));
    }

    private static string? GetString(JsonElement root, string name)
        => root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static int? GetInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
        {
            return null;
        }
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n))
        {
            return n;
        }
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
        {
            return s;
        }
        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement root, string name)
    {
        var raw = GetString(root, name);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        // Twilio dates are RFC 2822, e.g. "Fri, 24 May 2019 17:44:50 +0000".
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
        {
            return dto;
        }
        string[] formats =
        {
            "ddd, dd MMM yyyy HH:mm:ss zzz",
            "ddd, dd MMM yyyy HH:mm:ss K",
            "ddd, d MMM yyyy HH:mm:ss zzz"
        };
        if (DateTimeOffset.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var exact))
        {
            return exact;
        }
        return null;
    }

    // ---- errors --------------------------------------------------------------------------------

    private static TwilioApiException BuildApiException(HttpStatusCode statusCode, string payload)
    {
        int? code = null;
        string message = $"Twilio API request failed with status {(int)statusCode}.";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            code = GetInt(root, "code");
            var providerMessage = GetString(root, "message");
            if (!string.IsNullOrEmpty(providerMessage))
            {
                message = Sanitize(providerMessage!);
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message (and never echo a raw body that could carry a number).
        }
        return new TwilioApiException((int)statusCode, code, message);
    }

    /// <summary>Strip phone-number-like digit runs so a provider message can be logged safely.</summary>
    private static string Sanitize(string text) => PhoneLikeRegex.Replace(text, "[redacted]");
}
