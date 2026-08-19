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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio implementation of <see cref="ISmsGateway"/> over the REST API using plain HTTP.
///
/// Messaging (send / read / cancel / redact / list) uses the v2010 Messages resource at
/// <c>{BaseUrl or api.twilio.com}/2010-04-01/Accounts/{AccountSid}/Messages...</c>. Number
/// validation uses Lookup v2 at <c>lookups.twilio.com</c>, a different host that the
/// <c>Twilio:BaseUrl</c> override deliberately does not touch.
///
/// The auth token, destination numbers and message bodies are never written to logs.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const string DefaultMessagingBase = "https://api.twilio.com";
    private const string LookupBase = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;
    private readonly string _messagingBase;
    private readonly string _authHeader;

    public TwilioSmsGateway(HttpClient http, IOptions<TwilioSettings> options, IAppLogger<TwilioSmsGateway> logger)
    {
        _http = http;
        _settings = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }

        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBase
            : _settings.BaseUrl!.TrimEnd('/');

        _authHeader = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
    }

    public string FromNumber => _settings.FromNumber;

    // ---- Lookup v2 (separate host) ------------------------------------------------------------

    public async Task<PhoneLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken)
    {
        var url = $"{LookupBase}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var request = NewRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        // A malformed / non-existent number comes back as 404 — treat as simply not valid.
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneLookupResult(false, null);
        }
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderError(response, "lookup", cancellationToken);
        }

        using var doc = await ReadJson(response, cancellationToken);
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        string? e164 = root.TryGetProperty("phone_number", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
        return new PhoneLookupResult(valid, e164);
    }

    // ---- Messages resource --------------------------------------------------------------------

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
        => CreateMessageAsync(new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        }, cancellationToken);

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateMessageAsync(new Dictionary<string, string>
        {
            ["To"] = toE164,
            // Scheduling requires a Messaging Service; Twilio selects the sender from its pool.
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        }, cancellationToken);

    private async Task<SmsSendResult> CreateMessageAsync(IDictionary<string, string> form, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
        using var request = NewRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderError(response, "create message", cancellationToken);
        }
        using var doc = await ReadJson(response, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    public async Task<SmsSendResult?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var request = NewRequest(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderError(response, "read message", cancellationToken);
        }
        using var doc = await ReadJson(response, cancellationToken);
        return ParseMessage(doc.RootElement);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";

        // A message scheduled moments ago may briefly return 404 on cancel due to Twilio's
        // eventual consistency. Cancelling a follow-up must be reliable — a cancelled order must
        // never trigger the "how did it go?" message — so retry a bounded number of times on 404.
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            using var request = NewRequest(HttpMethod.Post, url);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            if (response.StatusCode == HttpStatusCode.NotFound && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }
            await ThrowProviderError(response, "cancel message", cancellationToken);
        }
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(providerMessageSid)}.json";
        using var request = NewRequest(HttpMethod.Post, url);
        // Updating the body to an empty string redacts it at Twilio while keeping the record.
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            await ThrowProviderError(response, "redact message", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        // Ask the provider for this sending number's messages directly. The date window is widened
        // by a day on each side to be robust to date-granular server-side filtering, then refined
        // to the exact range client-side so the whole range is covered precisely.
        var afterDate = from.ToUniversalTime().AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var beforeDate = to.ToUniversalTime().AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var next = $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json"
                   + $"?From={Uri.EscapeDataString(fromNumber)}&PageSize=1000"
                   + $"&DateSent%3E={afterDate}&DateSent%3C={beforeDate}";

        var results = new List<ProviderMessage>();
        var safety = 0;
        while (!string.IsNullOrEmpty(next) && safety++ < 100)
        {
            using var request = NewRequest(HttpMethod.Get, next);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                await ThrowProviderError(response, "list messages", cancellationToken);
            }
            using var doc = await ReadJson(response, cancellationToken);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    // Filtering the Messages list by From returns both the outbound sends and, when
                    // the destination is itself a number on this account, the inbound receive-leg
                    // echo of the same message. Reconciliation is about messages this app SENT, so
                    // keep only outbound records.
                    var direction = GetString(m, "direction");
                    if (direction is not null && !direction.StartsWith("outbound", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var basic = ParseMessage(m);
                    var effective = ParseDate(m, "date_sent") ?? ParseDate(m, "date_created");
                    if (effective is null || (effective >= from && effective <= to))
                    {
                        results.Add(new ProviderMessage(
                            basic.Sid,
                            GetString(m, "to"),
                            GetString(m, "from"),
                            basic.Status,
                            basic.ErrorCode,
                            effective));
                    }
                }
            }

            next = root.TryGetProperty("next_page_uri", out var np) && np.ValueKind == JsonValueKind.String
                ? AbsoluteFromMessagingBase(np.GetString())
                : null;
        }

        return results;
    }

    // ---- helpers ------------------------------------------------------------------------------

    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authHeader);
        return request;
    }

    private static SmsSendResult ParseMessage(JsonElement m)
    {
        var sid = GetString(m, "sid") ?? string.Empty;
        var status = GetString(m, "status") ?? string.Empty;
        int? errorCode = null;
        if (m.TryGetProperty("error_code", out var ec) && ec.ValueKind == JsonValueKind.Number && ec.TryGetInt32(out var code))
        {
            errorCode = code;
        }
        return new SmsSendResult(sid, status, errorCode);
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? ParseDate(JsonElement e, string name)
    {
        var s = GetString(e, name);
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? dto
            : null;
    }

    private string? AbsoluteFromMessagingBase(string? relativeOrAbsolute)
    {
        if (string.IsNullOrEmpty(relativeOrAbsolute))
        {
            return null;
        }
        if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out _))
        {
            return relativeOrAbsolute;
        }
        return new Uri(new Uri(_messagingBase), relativeOrAbsolute).ToString();
    }

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Throw with the HTTP status and Twilio's numeric error code only. The provider's error text
    /// can echo the destination number, so it is deliberately not logged or surfaced.
    /// </summary>
    private async Task ThrowProviderError(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        int? twilioCode = null;
        try
        {
            using var doc = await ReadJson(response, cancellationToken);
            if (doc.RootElement.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out var code))
            {
                twilioCode = code;
            }
        }
        catch
        {
            // ignore body parse failures
        }

        _logger.LogWarning($"Twilio {operation} failed: HTTP {(int)response.StatusCode}, code {twilioCode?.ToString() ?? "n/a"}.");
        throw new HttpRequestException($"Twilio {operation} failed with HTTP {(int)response.StatusCode} (code {twilioCode?.ToString() ?? "n/a"}).");
    }
}
