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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed ISmsService. Talks to the messaging API (api.twilio.com, or the
/// configured Twilio:BaseUrl override) for sending/reading/reconciling messages, and to
/// the Lookup API (lookups.twilio.com) for number validation. Never logs destination
/// phone numbers or credentials.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";
    private const string ApiVersionPath = "/2010-04-01";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are missing. Configure Twilio:AccountSid and Twilio:AuthToken via user-secrets or environment variables.");
        }
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesCollectionUrl =>
        $"{MessagingBaseUrl}{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string messageSid) =>
        $"{MessagingBaseUrl}{ApiVersionPath}/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // The Lookup API is served from its own host; Twilio:BaseUrl does not govern it.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = NewRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if ((int)response.StatusCode == 404)
        {
            return new PhoneNumberValidationResult(false, null, new[] { "NOT_A_NUMBER" });
        }

        var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        var valid = root.TryGetProperty("valid", out var validElement) && validElement.GetBoolean();
        var canonical = root.TryGetProperty("phone_number", out var phoneElement) ? phoneElement.GetString() : null;
        var errors = root.TryGetProperty("validation_errors", out var errorsElement) && errorsElement.ValueKind == JsonValueKind.Array
            ? errorsElement.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToArray()
            : Array.Empty<string>();

        return new PhoneNumberValidationResult(valid, valid ? canonical : null, errors);
    }

    public async Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesCollectionUrl, form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new SmsProviderException("Twilio:MessagingServiceSid is required to schedule messages with the provider.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesCollectionUrl, form, cancellationToken);
    }

    public async Task<SmsMessageState> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = NewRequest(HttpMethod.Get, MessageUrl(messageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var document = await ReadJsonAsync(response, cancellationToken);
        return ParseMessageState(document.RootElement);
    }

    public async Task<SmsMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        var result = await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
        return new SmsMessageState(result.MessageSid, result.Status, null, null, null);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // The From filter is sent to the provider so only this application's own sending
        // number's traffic is returned; the account's other traffic never leaves Twilio.
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(FormatUtc(from))}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(FormatUtc(to))}",
            "PageSize=1000"
        });

        var records = new List<SmsMessageRecord>();
        string? nextUrl = $"{MessagesCollectionUrl}?{query}";

        while (nextUrl != null)
        {
            using var request = NewRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var document = await ReadJsonAsync(response, cancellationToken);
            var root = document.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    records.Add(new SmsMessageRecord(
                        message.GetProperty("sid").GetString() ?? string.Empty,
                        message.TryGetProperty("from", out var f) ? f.GetString() ?? string.Empty : string.Empty,
                        message.TryGetProperty("to", out var t) ? t.GetString() ?? string.Empty : string.Empty,
                        message.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
                        ParseDate(message, "date_sent"),
                        ParseDate(message, "date_created"),
                        ParseNullableInt(message, "error_code"),
                        message.TryGetProperty("error_message", out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() : null));
                }
            }

            nextUrl = null;
            if (root.TryGetProperty("next_page_uri", out var nextPage) && nextPage.ValueKind == JsonValueKind.String)
            {
                var nextPageUri = nextPage.GetString();
                if (!string.IsNullOrEmpty(nextPageUri))
                {
                    // next_page_uri is relative to the messaging host; rebase it onto the
                    // configured messaging base so the override governs pagination too.
                    nextUrl = new Uri(new Uri(MessagingBaseUrl + "/"), nextPageUri.TrimStart('/')).ToString();
                }
            }
        }

        return records;
    }

    private async Task<SmsSendResult> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var request = NewRequest(HttpMethod.Post, url);
        request.Content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        return new SmsSendResult(
            root.GetProperty("sid").GetString() ?? string.Empty,
            root.TryGetProperty("status", out var status) ? status.GetString() ?? string.Empty : string.Empty);
    }

    private HttpRequestMessage NewRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return request;
    }

    private async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            int? providerCode = null;
            try
            {
                using var errorDocument = JsonDocument.Parse(content);
                if (errorDocument.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number)
                {
                    providerCode = codeElement.GetInt32();
                }
            }
            catch (JsonException)
            {
                // Non-JSON error body; the HTTP status is enough.
            }

            // Deliberately excludes the provider's message text, which can echo the destination number.
            _logger.LogWarning("Twilio request to {Url} failed with HTTP {StatusCode} (provider error code {ErrorCode})",
                response.RequestMessage?.RequestUri?.AbsolutePath, (int)response.StatusCode, providerCode);
            throw new SmsProviderException($"Twilio request failed with HTTP {(int)response.StatusCode}.", providerCode);
        }

        return JsonDocument.Parse(content);
    }

    private static SmsMessageState ParseMessageState(JsonElement message)
    {
        return new SmsMessageState(
            message.GetProperty("sid").GetString() ?? string.Empty,
            message.TryGetProperty("status", out var s) ? s.GetString() ?? string.Empty : string.Empty,
            ParseNullableInt(message, "error_code"),
            message.TryGetProperty("error_message", out var em) && em.ValueKind == JsonValueKind.String ? em.GetString() : null,
            ParseDate(message, "date_sent"));
    }

    private static int? ParseNullableInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }
        return null;
    }

    private static DateTimeOffset? ParseDate(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
