using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="ISmsMessagingService"/>, talking to the provider's REST
/// API over plain HTTP. Messaging calls (send/read/update/list) go to the messaging host —
/// <c>Twilio:BaseUrl</c> when set, otherwise api.twilio.com. Number validation uses the Lookup
/// API on its own host, which <c>Twilio:BaseUrl</c> does not govern.
///
/// The auth token and destination numbers are never logged. Phone numbers are only ever placed
/// in outbound request bodies/URLs to the provider.
/// </summary>
public class TwilioMessagingService : ISmsMessagingService
{
    private const string DefaultMessagingHost = "https://api.twilio.com";
    private const string LookupHost = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;

    public TwilioMessagingService(HttpClient http, IOptions<TwilioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    public string FromNumber => _settings.FromNumber;

    private string MessagingHost =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingHost : _settings.BaseUrl!.TrimEnd('/');

    private string AccountResource => $"{MessagingHost}/2010-04-01/Accounts/{_settings.AccountSid}";

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        // Lookup v2 is served from lookups.twilio.com and is NOT governed by Twilio:BaseUrl.
        var url = $"{LookupHost}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        using var response = await _http.GetAsync(url, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult(false, null, "not_found");
        }

        if (!response.IsSuccessStatusCode)
        {
            // 400 with a 60xxx code means the provider could not treat the input as a number.
            return new PhoneNumberValidationResult(false, null, $"lookup_http_{(int)response.StatusCode}");
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var numberEl) && numberEl.ValueKind == JsonValueKind.String
            ? numberEl.GetString()
            : null;

        if (!valid || string.IsNullOrEmpty(canonical))
        {
            return new PhoneNumberValidationResult(false, canonical, "not_a_usable_destination");
        }

        return new PhoneNumberValidationResult(true, canonical, null);
    }

    public Task<SentMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public Task<SentMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service SID; the provider holds the message and sends
        // it at SendAt. SendAt must be ISO-8601 UTC and 15 min – 35 days in the future.
        var form = new Dictionary<string, string>
        {
            ["To"] = toE164,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAtUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["Body"] = body
        };
        return CreateMessageAsync(form, cancellationToken);
    }

    public async Task<SentMessageResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await PostFormAsync($"{AccountResource}/Messages/{messageSid}.json", form, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException((int)response.StatusCode, ExtractErrorCode(content));
        }
        return ParseSentMessage(content);
    }

    public async Task<MessageDeliveryState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"{AccountResource}/Messages/{messageSid}.json", cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException((int)response.StatusCode, ExtractErrorCode(content));
        }

        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        return new MessageDeliveryState(
            ReadString(root, "status") ?? string.Empty,
            ReadErrorCode(root),
            ReadDate(root, "date_sent"));
    }

    public async Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // POSTing an empty Body redacts the text at the provider while keeping the message record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync($"{AccountResource}/Messages/{messageSid}.json", form, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The message is already gone at the provider — the content is not retrievable, which
            // is exactly the intended end state.
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new TwilioMessagingException((int)response.StatusCode, ExtractErrorCode(content));
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // The provider's DateSent filter is day-granular, so widen the day bounds and trim to the
        // exact window afterwards. The From filter is applied at the provider (not post-hoc) so
        // only this application's own sending number is ever returned.
        var fromDay = fromUtc.UtcDateTime.Date.AddDays(-1);
        var toDay = toUtc.UtcDateTime.Date.AddDays(1);

        var query =
            $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={fromDay:yyyy-MM-dd}" +   // DateSent>
            $"&DateSent%3C={toDay:yyyy-MM-dd}" +     // DateSent<
            $"&PageSize=1000";
        var url = $"{AccountResource}/Messages.json?{query}";

        var collected = new List<ProviderMessage>();
        var safetyPageLimit = 1000;

        while (!string.IsNullOrEmpty(url) && safetyPageLimit-- > 0)
        {
            using var response = await _http.GetAsync(url, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new TwilioMessagingException((int)response.StatusCode, ExtractErrorCode(content));
            }

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    collected.Add(new ProviderMessage(
                        ReadString(message, "sid") ?? string.Empty,
                        ReadString(message, "to"),
                        ReadString(message, "from"),
                        ReadString(message, "status") ?? string.Empty,
                        ReadErrorCode(message),
                        ReadDate(message, "date_sent")));
                }
            }

            var next = root.TryGetProperty("next_page_uri", out var nextEl) && nextEl.ValueKind == JsonValueKind.String
                ? nextEl.GetString()
                : null;
            url = string.IsNullOrEmpty(next) ? null : $"{MessagingHost}{next}";
        }

        var results = new List<ProviderMessage>();
        foreach (var message in collected)
        {
            if (message.DateSent.HasValue && message.DateSent.Value >= fromUtc && message.DateSent.Value <= toUtc)
            {
                results.Add(message);
            }
        }
        return results;
    }

    private async Task<SentMessageResult> CreateMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await PostFormAsync($"{AccountResource}/Messages.json", form, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioMessagingException((int)response.StatusCode, ExtractErrorCode(content));
        }
        return ParseSentMessage(content);
    }

    private Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        return _http.SendAsync(request, cancellationToken);
    }

    private static SentMessageResult ParseSentMessage(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        return new SentMessageResult(
            ReadString(root, "sid"),
            ReadString(root, "status") ?? string.Empty,
            ReadErrorCode(root),
            ReadDate(root, "date_sent"));
    }

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadErrorCode(JsonElement element)
    {
        if (!element.TryGetProperty("error_code", out var value))
        {
            return null;
        }
        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            _ => null
        };
    }

    private static DateTimeOffset? ReadDate(JsonElement element, string property)
    {
        var raw = ReadString(element, property);
        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ExtractErrorCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("code", out var code))
            {
                return code.ValueKind == JsonValueKind.Number ? code.GetRawText() : code.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body — surface only the HTTP status via the caller.
        }
        return null;
    }
}
