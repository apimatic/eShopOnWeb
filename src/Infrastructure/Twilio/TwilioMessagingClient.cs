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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The provider gateway for the messaging API. Talks the documented REST contract directly:
/// HTTP Basic auth (Account SID / Auth Token), form-encoded requests, JSON responses, the Account SID
/// as a path parameter, and the base host taken from <c>Twilio:BaseUrl</c> when set. This is the only
/// type that sends, reads, cancels, redacts or lists messages. It never logs the auth token or a
/// destination number.
/// </summary>
public class TwilioMessagingClient : ISmsProvider
{
    private const string OutboundApiVersion = "2010-04-01";
    private const int MaxPages = 1000;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredSenderNumber => _settings.FromNumber;

    private string BaseUrl => _settings.ResolveMessagingBaseUrl();
    private string MessagesCollectionUrl => $"{BaseUrl}/{OutboundApiVersion}/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageResourceUrl(string sid) => $"{BaseUrl}/{OutboundApiVersion}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling a fixed future send requires a Messaging Service on the create request.
        var form = new Dictionary<string, string>
        {
            ["To"] = toPhoneNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, cancellationToken);
    }

    private async Task<SmsSendResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(HttpMethod.Post, MessagesCollectionUrl, form);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsProviderException("Transport failure while creating a message at the provider.", ex);
        }

        using var _ = response;
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var result = new SmsSendResult(
                ReadString(root, "sid"),
                ReadString(root, "status") ?? "unknown",
                ReadInt(root, "error_code"),
                ReadString(root, "error_message"));
            _logger.LogInformation("Provider accepted message {Sid} with status {Status}.", result.ProviderMessageSid, result.Status);
            return result;
        }

        // A documented request-level rejection: no resource was created. Surface it as an outcome.
        if ((int)response.StatusCode >= 400 && (int)response.StatusCode < 500)
        {
            var (code, message) = ReadProviderError(payload);
            _logger.LogWarning("Provider rejected a message create with status {Http} (code {Code}).", (int)response.StatusCode, code);
            return new SmsSendResult(null, "failed", code, message);
        }

        throw new SmsProviderException($"Provider returned {(int)response.StatusCode} creating a message.");
    }

    public async Task<SmsMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(HttpMethod.Get, MessageResourceUrl(providerMessageSid), form: null);
        using var response = await SendCoreAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SmsProviderException($"Provider returned {(int)response.StatusCode} fetching message {providerMessageSid}.");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsMessageState(
            ReadString(root, "sid") ?? providerMessageSid,
            ReadString(root, "status") ?? "unknown",
            ReadInt(root, "error_code"),
            ReadString(root, "error_message"));
    }

    public async Task<SmsMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var request = BuildRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await SendCoreAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new SmsProviderException($"Provider returned {(int)response.StatusCode} cancelling message {providerMessageSid}.");

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        _logger.LogInformation("Cancelled scheduled message {Sid}.", providerMessageSid);
        return new SmsMessageState(
            ReadString(root, "sid") ?? providerMessageSid,
            ReadString(root, "status") ?? "canceled",
            ReadInt(root, "error_code"),
            ReadString(root, "error_message"));
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redaction is an empty Body on the update: the text is disposed of, the record survives.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var request = BuildRequest(HttpMethod.Post, MessageResourceUrl(providerMessageSid), form);
        using var response = await SendCoreAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (code, _) = ReadProviderError(payload);
            throw new SmsProviderException($"Provider returned {(int)response.StatusCode} (code {code}) redacting message {providerMessageSid}.");
        }
        _logger.LogInformation("Redacted content of message {Sid}.", providerMessageSid);
    }

    public async Task<IReadOnlyList<ProviderMessageSummary>> ListOutboundFromConfiguredSenderAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessageSummary>();

        // The provider's date filters are date-granular and anchored at midnight GMT: `DateSent<` with a date
        // means "before that day's midnight", which would drop everything sent later on the `to` day. So ask
        // for the inclusive day span [from-day .. to-day+1) — a superset — and let the caller narrow to the
        // exact instant range afterwards.
        var lowerDate = fromUtc.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var upperDate = toUtc.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={lowerDate}&DateSent%3C={upperDate}&PageSize=1000";
        var nextUrl = MessagesCollectionUrl + query;

        for (var page = 0; page < MaxPages && nextUrl is not null; page++)
        {
            using var request = BuildRequest(HttpMethod.Get, nextUrl, form: null);
            using var response = await SendCoreAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new SmsProviderException($"Provider returned {(int)response.StatusCode} listing messages.");

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessageSummary(
                        ReadString(m, "sid") ?? string.Empty,
                        ReadString(m, "status") ?? "unknown",
                        ReadString(m, "to"),
                        ReadString(m, "from"),
                        ReadRfc2822Date(ReadString(m, "date_sent")),
                        ReadInt(m, "error_code")));
                }
            }

            var next = ReadString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : $"{BaseUrl}{next}";
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsProviderException("Transport failure talking to the provider messaging API.", ex);
        }
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string url, IReadOnlyDictionary<string, string>? form)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (form is not null)
            request.Content = new FormUrlEncodedContent(form);
        return request;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(value.GetString(), out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ReadRfc2822Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // The /2010-04-01 host returns RFC 2822 timestamps, e.g. "Thu, 24 Aug 2023 05:01:45 +0000".
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static (int? code, string? message) ReadProviderError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return (ReadInt(root, "code"), ReadString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
