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
using Microsoft.eShopWeb.ApplicationCore.Sms;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsGateway"/> over plain HTTPS.
///
/// Every message is sent through the Messaging Service (<c>MessagingServiceSid</c>) whose sender pool
/// is eShop's configured <c>FromNumber</c>. That single choice gives all three properties the flows
/// need: the sender on every message is <c>FromNumber</c> (so reconciliation-by-<c>From</c> is complete),
/// scheduling works (Twilio requires a Messaging Service and forbids <c>From</c> for scheduled sends),
/// and immediate/scheduled sends behave consistently.
///
/// The messaging API base address honours <c>Twilio:BaseUrl</c> when set; the Lookup API always uses
/// its own host and is not affected by that override.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    public const string HttpClientName = "twilio";
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private const string LookupBase = "https://lookups.twilio.com";
    private const int MaxReconciliationPages = 1000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly string _authHeader;
    private readonly string _messagingBase;

    public TwilioSmsGateway(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBase
            : _settings.BaseUrl!.TrimEnd('/');
    }

    private string MessagesUrl => $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    // ---- Lookup (validation + canonical E.164) ----------------------------------------------

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await SendWithRetryAsync(() => CreateRequest(HttpMethod.Get, url), retryOnTransient: true, cancellationToken);

        // A number Twilio cannot even parse comes back as 404; treat that as "not usable", not a fault.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new PhoneLookupResult(false, null, "The number is not a valid phone number.");

        var payload = await ReadOrThrowAsync(response, "Phone number lookup failed", cancellationToken);
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        string? canonical = root.TryGetProperty("phone_number", out var pnEl) && pnEl.ValueKind == JsonValueKind.String
            ? pnEl.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
            return new PhoneLookupResult(false, null, "The number is not a usable destination.");

        return new PhoneLookupResult(true, canonical, null);
    }

    // ---- Send (immediate or scheduled) ------------------------------------------------------

    public async Task<SmsSendResult> SendAsync(string toE164, string body, DateTimeOffset? scheduleAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("To", toE164),
            new("Body", body)
        };
        if (scheduleAt.HasValue)
        {
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", scheduleAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        // Writes are not auto-retried: a retry after a timeout could send a duplicate message.
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Post, MessagesUrl, form), retryOnTransient: false, cancellationToken);
        var payload = await ReadOrThrowAsync(response, "Message send", cancellationToken);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var sid = GetString(root, "sid");
        var status = GetString(root, "status") ?? "queued";
        var errorCode = GetInt(root, "error_code");

        if (string.IsNullOrEmpty(sid))
            throw new SmsGatewayException("Message send did not return a provider identifier.");

        return new SmsSendResult(sid!, status, errorCode);
    }

    // ---- Fetch a single message's status ----------------------------------------------------

    public async Task<SmsStatusResult> FetchStatusAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, MessageUrl(providerSid)), retryOnTransient: true, cancellationToken);
        var payload = await ReadOrThrowAsync(response, "Message status fetch", cancellationToken);

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsStatusResult(GetString(root, "status") ?? "unknown", GetInt(root, "error_code"));
    }

    // ---- Cancel a scheduled message ---------------------------------------------------------

    public async Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Post, MessageUrl(providerSid), form), retryOnTransient: false, cancellationToken);
        await ReadOrThrowAsync(response, "Scheduled message cancellation", cancellationToken);
    }

    // ---- Redact content ---------------------------------------------------------------------

    public async Task RedactContentAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // Updating the body to empty redacts the content at Twilio while the record and status remain.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Post, MessageUrl(providerSid), form), retryOnTransient: false, cancellationToken);
        await ReadOrThrowAsync(response, "Message content redaction", cancellationToken);
    }

    // ---- List sent messages for reconciliation ----------------------------------------------

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var from = fromUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var to = toUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Ask the provider for THIS number's messages in the range (server-side From filter), not a
        // wider answer filtered afterwards. DateSent>=/<= are expressed with the operator in the key.
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(from)}" +
                    $"&DateSent%3C={Uri.EscapeDataString(to)}" +
                    "&PageSize=1000";
        var nextUrl = MessagesUrl + query;

        var results = new List<ProviderMessageRecord>();
        var pages = 0;
        while (!string.IsNullOrEmpty(nextUrl) && pages < MaxReconciliationPages)
        {
            pages++;
            using var response = await SendWithRetryAsync(
                () => CreateRequest(HttpMethod.Get, nextUrl!), retryOnTransient: true, cancellationToken);
            var payload = await ReadOrThrowAsync(response, "Message reconciliation listing", cancellationToken);

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    var sid = GetString(m, "sid");
                    if (string.IsNullOrEmpty(sid))
                        continue;
                    DateTimeOffset? dateSent = null;
                    var dateSentStr = GetString(m, "date_sent");
                    if (!string.IsNullOrEmpty(dateSentStr) &&
                        DateTimeOffset.TryParse(dateSentStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
                        dateSent = parsed;

                    results.Add(new ProviderMessageRecord(
                        sid!, GetString(m, "status") ?? "unknown", GetString(m, "from"), GetString(m, "to"),
                        dateSent, GetInt(m, "error_code")));
                }
            }

            // Follow next_page_uri (a relative path) to cover the whole range, honouring any base override.
            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String)
            {
                var next = nextEl.GetString();
                if (!string.IsNullOrEmpty(next))
                    nextUrl = _messagingBase + next;
            }
        }

        return results;
    }

    // ---- HTTP plumbing ----------------------------------------------------------------------

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, IEnumerable<KeyValuePair<string, string>>? form = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(Func<HttpRequestMessage> requestFactory, bool retryOnTransient, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var request = requestFactory();
                response = await client.SendAsync(request, cancellationToken);
                var transient = (int)response.StatusCode == 429 || (int)response.StatusCode >= 500;
                if (retryOnTransient && transient && attempt < maxAttempts)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
                    continue;
                }
                return response;
            }
            catch (HttpRequestException) when (retryOnTransient && attempt < maxAttempts)
            {
                response?.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    /// <summary>
    /// Returns the body on success; otherwise throws a gateway exception whose message deliberately
    /// omits Twilio's own error text (which can echo the destination number) and any phone number.
    /// </summary>
    private static async Task<string> ReadOrThrowAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
            return body;

        int? twilioCode = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            twilioCode = GetInt(doc.RootElement, "code");
        }
        catch (JsonException) { /* non-JSON error body; ignore */ }

        var codeText = twilioCode.HasValue ? $" (provider code {twilioCode})" : string.Empty;
        throw new SmsGatewayException($"{operation} failed with HTTP {(int)response.StatusCode}{codeText}.");
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt32(),
            JsonValueKind.String when int.TryParse(value.GetString(), out var n) => n,
            _ => null
        };
    }
}
