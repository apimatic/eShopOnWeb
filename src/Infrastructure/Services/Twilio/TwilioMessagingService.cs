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
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Talks to Twilio's messaging (Programmable Messaging / 2010-04-01) API and Lookup API over HTTP,
/// using HTTP Basic auth (Account SID + Auth Token). Only the messaging API honours the optional
/// <see cref="TwilioSettings.BaseUrl"/> override; Lookup is served from its own host.
/// </summary>
public class TwilioMessagingService : ISmsMessagingService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingService> _logger;

    private readonly string _messagingBase;
    private readonly string _messagingAuthority;

    public TwilioMessagingService(HttpClient http, IOptions<TwilioSettings> settings, IAppLogger<TwilioMessagingService> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;

        _messagingBase = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');
        _messagingAuthority = new Uri(_messagingBase).GetLeftPart(UriPartial.Authority);

        // HTTP Basic: Account SID as username, Auth Token as password. The token is never logged.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup lives on its own host and is not governed by the messaging BaseUrl override.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider could not parse the number into a real destination.
            return PhoneNumberValidationResult.Invalid();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio lookup failed with HTTP {0}.", (int)response.StatusCode);
            throw new SmsProviderException($"Number validation failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode);
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var canonical = ReadString(root, "phone_number");

        if (valid && !string.IsNullOrEmpty(canonical))
        {
            return PhoneNumberValidationResult.Valid(canonical);
        }

        return PhoneNumberValidationResult.Invalid();
    }

    public async Task<SmsMessageResult> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", _settings.FromNumber),
            new("Body", body)
        };

        using var json = await PostFormAsync(MessagesCollectionUrl(), form, "send-message", cancellationToken);
        return ParseMessage(json!.RootElement);
    }

    public async Task<SmsMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service. Pin the configured From so the scheduled send stays
        // consistent with immediate sends and is visible to reconciliation.
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("From", _settings.FromNumber),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            new("Body", body)
        };

        using var json = await PostFormAsync(MessagesCollectionUrl(), form, "schedule-message", cancellationToken);
        var result = ParseMessage(json!.RootElement);
        return new SmsMessageResult
        {
            Sid = result.Sid,
            Status = result.Status,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage,
            ScheduledFor = sendAt
        };
    }

    public async Task<SmsMessageResult> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstanceUrl(sid));
        using var json = await SendReadingJsonAsync(request, "fetch-message", cancellationToken);
        return ParseMessage(json!.RootElement);
    }

    public async Task<SmsMessageResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var json = await PostFormAsync(MessageInstanceUrl(sid), form, "cancel-scheduled-message", cancellationToken);
        return ParseMessage(json!.RootElement);
    }

    public async Task RedactAsync(string sid, CancellationToken cancellationToken = default)
    {
        // Redacting the body at the provider: send Body as an empty string.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var _ = await PostFormAsync(MessageInstanceUrl(sid), form, "redact-message", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider directly for this application's own sending number's messages, over a date
        // range that generously covers the requested window; then trim to the exact date-times in memory.
        var fromDate = from.UtcDateTime.Date.AddDays(-1);
        var toDate = to.UtcDateTime.Date.AddDays(1);

        var query = new StringBuilder();
        query.Append("?From=").Append(Uri.EscapeDataString(_settings.FromNumber));
        query.Append("&DateSent%3E=").Append(fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append("&DateSent%3C=").Append(toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        query.Append("&PageSize=1000");

        var results = new List<ProviderMessage>();
        string? nextUrl = MessagesCollectionUrl() + query;

        while (!string.IsNullOrEmpty(nextUrl))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, nextUrl);
            using var json = await SendReadingJsonAsync(request, "list-messages", cancellationToken);
            var root = json!.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var dateSent = ParseProviderDate(ReadString(message, "date_sent"));

                    // Precise, inclusive trim to the requested date-time range. Messages not yet sent
                    // (no date_sent) are not part of a "what was sent" reconciliation.
                    if (dateSent is null || dateSent < from || dateSent > to)
                    {
                        continue;
                    }

                    results.Add(new ProviderMessage
                    {
                        Sid = ReadString(message, "sid") ?? string.Empty,
                        Status = ReadString(message, "status") ?? string.Empty,
                        From = ReadString(message, "from"),
                        To = ReadString(message, "to"),
                        DateSent = dateSent,
                        ErrorCode = ReadInt(message, "error_code"),
                        ErrorMessage = ReadString(message, "error_message")
                    });
                }
            }

            var next = ReadString(root, "next_page_uri");
            nextUrl = string.IsNullOrEmpty(next) ? null : _messagingAuthority + next;
        }

        return results;
    }

    // ----- URL builders --------------------------------------------------------------------------

    private string MessagesCollectionUrl() =>
        $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstanceUrl(string sid) =>
        $"{_messagingBase}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    // ----- HTTP helpers --------------------------------------------------------------------------

    private async Task<JsonDocument?> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form)
        };
        return await SendReadingJsonAsync(request, operation, cancellationToken);
    }

    private async Task<JsonDocument?> SendReadingJsonAsync(HttpRequestMessage request, string operation, CancellationToken cancellationToken)
    {
        using var response = await _http.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Log only the operation, HTTP status and the provider's numeric error code — never the body,
            // which can contain the destination number.
            var providerCode = TryReadErrorCode(content);
            _logger.LogWarning("Twilio {0} failed with HTTP {1} (provider code {2}).", operation, (int)response.StatusCode, providerCode?.ToString() ?? "n/a");
            throw new SmsProviderException($"Provider {operation} failed with HTTP {(int)response.StatusCode}.", (int)response.StatusCode, providerCode);
        }

        return string.IsNullOrWhiteSpace(content) ? null : JsonDocument.Parse(content);
    }

    // ----- parsing -------------------------------------------------------------------------------

    private static SmsMessageResult ParseMessage(JsonElement message) => new()
    {
        Sid = ReadString(message, "sid") ?? string.Empty,
        Status = ReadString(message, "status") ?? string.Empty,
        ErrorCode = ReadInt(message, "error_code"),
        ErrorMessage = ReadString(message, "error_message")
    };

    private static int? TryReadErrorCode(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(content);
            return ReadInt(json.RootElement, "code");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(property, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }
        return null;
    }

    private static int? ReadInt(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) => s,
            _ => null
        };
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        // Twilio returns RFC 2822 timestamps, e.g. "Fri, 30 Jul 2021 20:36:27 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
