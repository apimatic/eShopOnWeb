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
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's messaging API (send / schedule / cancel / fetch / redact / list) and to
/// Twilio Lookup for destination validation. The messaging base address honours the optional
/// <c>Twilio:BaseUrl</c> override; Lookup is served from its own host and is not governed by it.
///
/// Auth is HTTP Basic (Account SID as username, Auth Token as password), set per request and never
/// logged. Shoppers' numbers appear only in request bodies / lookup paths, never in log messages.
/// </summary>
public class TwilioMessagingGateway : ISmsNotificationGateway
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingGateway> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingGateway(
        HttpClient httpClient,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingGateway> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');
    }

    public async Task<PhoneNumberValidationResult> ValidateDestinationAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawNumber))
        {
            return PhoneNumberValidationResult.Unusable("A phone number is required.");
        }

        // Lookup is served from its own host and is NOT governed by Twilio:BaseUrl.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber.Trim())}";
        HttpResponseMessage response;
        string payload;
        try
        {
            using var request = CreateRequest(HttpMethod.Get, url);
            response = await _httpClient.SendAsync(request, cancellationToken);
            payload = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException)
        {
            // Deliberately do not surface the underlying message/URL: it carries the shopper's number.
            throw new SmsGatewayException("Could not reach the provider to validate the phone number.");
        }

        using var _ = response;

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return PhoneNumberValidationResult.Unusable("The number is not a recognisable phone number.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryReadError(payload);
            throw new SmsGatewayException(
                $"Phone-number lookup failed ({(int)response.StatusCode}): {message ?? response.ReasonPhrase}", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        if (!valid)
        {
            var reason = "The provider does not consider this a usable destination.";
            if (root.TryGetProperty("validation_errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
            {
                var parts = new List<string>();
                foreach (var e in errs.EnumerateArray())
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
            return PhoneNumberValidationResult.Unusable(reason);
        }

        var canonical = root.TryGetProperty("phone_number", out var pnEl) ? pnEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(canonical))
        {
            // Valid but no canonical form returned — cannot store a canonical number, so reject.
            return PhoneNumberValidationResult.Unusable("The provider returned no canonical form for the number.");
        }

        return PhoneNumberValidationResult.Usable(canonical);
    }

    public async Task<MessageDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await CreateMessageAsync(form, scheduledFor: null, cancellationToken);
    }

    public async Task<MessageDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging Service capability: address the service, ask for a fixed send
        // time, and let the provider hold the message until then. No explicit From — the service
        // selects the sender from its pool.
        var sendAtUtc = sendAt.ToUniversalTime();
        var form = new Dictionary<string, string>
        {
            ["To"] = toNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };
        return await CreateMessageAsync(form, scheduledFor: sendAtUtc, cancellationToken);
    }

    public async Task CancelScheduledAsync(string sid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{sid}.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Status"] = "canceled" });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryReadError(payload);
            throw new SmsGatewayException(
                $"Cancelling scheduled message failed ({(int)response.StatusCode}): {message ?? response.ReasonPhrase}", code);
        }
    }

    public async Task<MessageDispatchResult> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{sid}.json";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryReadError(payload);
            throw new SmsGatewayException(
                $"Fetching message failed ({(int)response.StatusCode}): {message ?? response.ReasonPhrase}", code);
        }
        return ParseMessage(payload);
    }

    public async Task DisposeContentAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Redact the body at the provider by updating it to an empty string. The message resource
        // and its delivery outcome survive; only the text is disposed of.
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{sid}.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Body"] = string.Empty });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = TryReadError(payload);
            throw new SmsGatewayException(
                $"Disposing of message content failed ({(int)response.StatusCode}): {message ?? response.ReasonPhrase}", code);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this sending number's messages over the range, rather than pulling a
        // wider answer and filtering afterwards. Date filters are widened to whole days (the filter
        // is date-granular) and the exact window is applied by the caller.
        var fromDate = from.ToUniversalTime().Date;
        var toDateExclusive = to.ToUniversalTime().Date.AddDays(1);

        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&{Uri.EscapeDataString("DateSent>")}={fromDate:yyyy-MM-dd}" +
            $"&{Uri.EscapeDataString("DateSent<")}={toDateExclusive:yyyy-MM-dd}" +
            "&PageSize=1000";

        var nextPath = $"/2010-04-01/Accounts/{AccountSid}/Messages.json?{query}";
        var results = new List<ProviderMessageRecord>();
        var safetyPages = 0;

        while (!string.IsNullOrEmpty(nextPath) && safetyPages++ < 1000)
        {
            var url = nextPath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? nextPath
                : $"{_messagingBaseUrl}{nextPath}";

            using var request = CreateRequest(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var (code, message) = TryReadError(payload);
                throw new SmsGatewayException(
                    $"Listing messages failed ({(int)response.StatusCode}): {message ?? response.ReasonPhrase}", code);
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessageRecord
                    {
                        Sid = GetString(m, "sid") ?? string.Empty,
                        To = GetString(m, "to"),
                        From = GetString(m, "from"),
                        Status = GetString(m, "status"),
                        ErrorCode = GetInt(m, "error_code"),
                        DateSent = ParseTwilioDate(GetString(m, "date_sent"))
                    });
                }
            }

            nextPath = root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String
                ? nextEl.GetString()
                : null;
        }

        return results;
    }

    private async Task<MessageDispatchResult> CreateMessageAsync(Dictionary<string, string> form, DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        var url = $"{_messagingBaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json";
        using var request = CreateRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            var result = ParseMessage(payload);
            return scheduledFor.HasValue ? result with { ScheduledFor = scheduledFor } : result;
        }

        // A 4xx/5xx with a provider error code is a recordable send outcome, not a transport
        // failure: return it as a failed dispatch so the caller can persist the provider's code and
        // still complete the underlying operation.
        var (code, message) = TryReadError(payload);
        if (code.HasValue || !string.IsNullOrEmpty(message))
        {
            return new MessageDispatchResult
            {
                Sid = null,
                Status = MessageDeliveryStatus.Failed,
                ErrorCode = code,
                ErrorMessage = message,
                ScheduledFor = scheduledFor
            };
        }

        throw new SmsGatewayException(
            $"Sending message failed ({(int)response.StatusCode}): {response.ReasonPhrase}");
    }

    private MessageDispatchResult ParseMessage(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new MessageDispatchResult
        {
            Sid = GetString(root, "sid"),
            Status = GetString(root, "status") ?? MessageDeliveryStatus.Queued,
            ErrorCode = GetInt(root, "error_code"),
            ErrorMessage = GetString(root, "error_message")
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string AccountSid => _settings.AccountSid;

    private static (int? code, string? message) TryReadError(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return (null, null);
        }
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return (GetInt(root, "code"), GetString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) => n,
            _ => null
        };
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
