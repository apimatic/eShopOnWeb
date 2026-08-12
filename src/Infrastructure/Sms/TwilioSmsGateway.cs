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
/// Talks to Twilio over HTTP, built directly against Twilio's OpenAPI contract. This is the only
/// place in the application that speaks to the provider. Two hosts are involved: the messaging API
/// (Account 2010-04-01), whose base address the <c>Twilio:BaseUrl</c> setting may override, and the
/// Lookup v2 API, which is served from its own host and is not governed by that setting.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupsBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioOptions _options;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _messagingBaseUrl;
    private readonly AuthenticationHeaderValue _authHeader;

    public TwilioSmsGateway(HttpClient http, IOptions<TwilioOptions> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;

        _messagingBaseUrl = (string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl!).TrimEnd('/');

        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        _authHeader = new AuthenticationHeaderValue("Basic", basic);
    }

    public string SendingNumber => _options.FromNumber;

    // ----- Lookup v2 : validate + canonicalise -----

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupsBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        // Twilio Lookup returns 404 (error 20404) for a number it cannot even parse; treat that, and a
        // 400, as "not a usable destination" rather than a provider fault.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_FOUND" });
        }

        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        var errors = new List<string>();
        if (root.TryGetProperty("validation_errors", out var errEl) && errEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in errEl.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                    errors.Add(e.GetString()!);
            }
        }

        return new PhoneNumberValidationResult(valid, canonical, errors);
    }

    // ----- Messaging : send / schedule -----

    public Task<SmsSubmissionResult> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SmsSubmissionResult> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling a fixed-time message requires a Messaging Service and ScheduleType=fixed with SendAt.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSubmissionResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);

        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        var sid = GetString(root, "sid") ?? throw new TwilioApiException(response.StatusCode, null, "Message response contained no sid.");
        var status = GetString(root, "status") ?? "queued";
        return new SmsSubmissionResult(sid, status);
    }

    // ----- Messaging : read a message's state -----

    public async Task<SmsDeliveryState> GetMessageStateAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = await ReadJsonAsync(response, cancellationToken);
        var root = doc.RootElement;
        var status = GetString(root, "status") ?? "unknown";
        int? errorCode = root.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number
            ? ec.GetInt32()
            : null;
        return new SmsDeliveryState(status, errorCode);
    }

    // ----- Messaging : cancel a scheduled message -----

    public async Task CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // ----- Messaging : redact a message body -----

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        // An empty Body redacts the message text at the provider while the record itself survives.
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    // ----- Messaging : list for reconciliation -----

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this sending number's messages over the range. The date bounds are sent
        // to the provider (day-granular, inclusive); the precise datetime window is applied afterwards.
        var fromDay = from.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDay = to.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"From={Uri.EscapeDataString(_options.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(fromDay)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(toDay)}" +
                    "&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        string? nextUrl = $"{_messagingBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json?{query}";

        var pages = 0;
        const int maxPages = 1000; // backstop against a runaway loop; far above any realistic range.
        while (nextUrl is not null && pages < maxPages)
        {
            pages++;
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            using var doc = await ReadJsonAsync(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = GetString(m, "sid");
                    if (sid is null)
                        continue;

                    var dateSent = ParseDate(GetString(m, "date_sent"));
                    // Precise datetime-window refinement (the provider filter is only day-granular).
                    if (dateSent is not null && (dateSent < from || dateSent > to))
                        continue;

                    results.Add(new ProviderMessageRecord(
                        sid,
                        GetString(m, "status"),
                        dateSent,
                        GetString(m, "to"),
                        GetString(m, "from")));
                }
            }

            var next = GetString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : CombineWithMessagingBase(next);
        }

        if (pages >= maxPages && nextUrl is not null)
        {
            _logger.LogWarning("Reconciliation stopped after {Pages} pages; the range may not be fully covered.", pages);
        }

        return results;
    }

    // ----- helpers -----

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = _authHeader;
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string CombineWithMessagingBase(string relativeOrAbsolute)
    {
        if (relativeOrAbsolute.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return relativeOrAbsolute;
        return $"{_messagingBaseUrl}/{relativeOrAbsolute.TrimStart('/')}";
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        int? providerErrorCode = null;
        var providerMessage = response.ReasonPhrase ?? "request failed";
        try
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(payload))
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number)
                    providerErrorCode = codeEl.GetInt32();
                if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    providerMessage = msgEl.GetString() ?? providerMessage;
            }
        }
        catch
        {
            // Non-JSON error body; fall back to the reason phrase. Never surfaces secrets.
        }

        // Logged without any recipient number or credential.
        _logger.LogWarning("Twilio API call failed with status {Status} (provider error {Code}).",
            (int)response.StatusCode, providerErrorCode);
        throw new TwilioApiException(response.StatusCode, providerErrorCode, providerMessage);
    }

    private static string? GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }
}
