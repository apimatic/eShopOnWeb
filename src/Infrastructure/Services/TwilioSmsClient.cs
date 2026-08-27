using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Twilio implementation of <see cref="ISmsClient"/>. Talks to the messaging API
/// (default https://api.twilio.com, overridable via Twilio:BaseUrl) for sending,
/// reading and reconciling messages, and to the Lookup API
/// (https://lookups.twilio.com — a separate host not governed by BaseUrl) for
/// number validation. Never logs phone numbers, message bodies or credentials.
/// </summary>
public class TwilioSmsClient : ISmsClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersionPath = "2010-04-01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are not configured. Provide Twilio:AccountSid and Twilio:AuthToken via user-secrets or environment configuration.");
        }

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string rawNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        // EscapeDataString percent-encodes a leading '+' as %2B, as the Lookup API requires.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false, ValidationErrors = new[] { "NOT_FOUND" } };
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var result = new PhoneNumberValidationResult
        {
            IsValid = root.TryGetProperty("valid", out var valid) && valid.GetBoolean(),
            CanonicalNumber = GetString(root, "phone_number"),
            NationalFormat = GetString(root, "national_format")
        };

        if (root.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            result.ValidationErrors = errors.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        return result;
    }

    public async Task<SmsSendResult> SendMessageAsync(string toE164, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("Body", body),
            new("From", _settings.FromNumber)
        };

        if (sendAtUtc.HasValue)
        {
            // Scheduling is a Messaging Service capability; the provider owns the schedule.
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await PostFormAsync(MessagesUrl(), form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorCode = TryGetErrorCode(json);
            _logger.LogWarning("Twilio rejected a message send with HTTP {StatusCode}, error code {ErrorCode}.", (int)response.StatusCode, errorCode);
            return new SmsSendResult { Success = false, Status = "failed", ErrorCode = errorCode };
        }

        using var doc = JsonDocument.Parse(json);
        return new SmsSendResult
        {
            Success = true,
            MessageSid = GetString(doc.RootElement, "sid"),
            Status = GetString(doc.RootElement, "status")
        };
    }

    public async Task<SmsMessageState?> FetchMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return ParseMessage(doc.RootElement);
    }

    public async Task<bool> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio rejected cancellation of message {MessageSid} with HTTP {StatusCode}.", providerMessageSid, (int)response.StatusCode);
            return false;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        return string.Equals(GetString(doc.RootElement, "status"), "canceled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio rejected redaction of message {MessageSid} with HTTP {StatusCode}.", providerMessageSid, (int)response.StatusCode);
        }

        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListMessagesFromSenderAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured; reconciliation must be scoped to this application's sending number.");
        }

        // The provider's DateSent filters are date-granular (YYYY-MM-DD, GMT) and the
        // inequality forms are strict, so the window is padded by a day on each side;
        // the exact date-time range is refined on the returned records below.
        var fromDate = fromUtc.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = toUtc.UtcDateTime.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={fromDate}",
            $"{Uri.EscapeDataString("DateSent<")}={toDate}",
            "PageSize=1000"
        });

        var messages = new List<SmsMessageState>();
        string? nextUri = $"{MessagesUrl()}?{query}";

        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var page) && page.ValueKind == JsonValueKind.Array)
            {
                messages.AddRange(page.EnumerateArray().Select(ParseMessage));
            }

            var nextPageUri = GetString(root, "next_page_uri");
            nextUri = string.IsNullOrEmpty(nextPageUri) ? null : _messagingBaseUrl + nextPageUri;
        }

        return messages
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when == null || (when >= fromUtc && when <= toUtc);
            })
            .ToList();
    }

    private string MessagesUrl() => $"{_messagingBaseUrl}/{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private Task<HttpResponseMessage> PostFormAsync(string url, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        // Not disposed here: the content must outlive the returned task.
        var content = new FormUrlEncodedContent(form);
        return _httpClient.PostAsync(url, content, cancellationToken);
    }

    private static SmsMessageState ParseMessage(JsonElement element)
    {
        return new SmsMessageState
        {
            MessageSid = GetString(element, "sid") ?? string.Empty,
            Status = GetString(element, "status"),
            ErrorCode = GetInt(element, "error_code"),
            To = GetString(element, "to"),
            From = GetString(element, "from"),
            Body = GetString(element, "body"),
            DateCreated = GetRfc2822Date(element, "date_created"),
            DateSent = GetRfc2822Date(element, "date_sent")
        };
    }

    private static string? GetString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var i) => i,
            JsonValueKind.String when int.TryParse(value.GetString(), out var i) => i,
            _ => null
        };
    }

    private static DateTimeOffset? GetRfc2822Date(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        return raw != null && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static int? TryGetErrorCode(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return GetInt(doc.RootElement, "code");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
