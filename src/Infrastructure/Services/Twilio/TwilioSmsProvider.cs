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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio implementation of the messaging provider contract, over plain HTTPS.
/// Messaging-API calls (send, read, cancel, redact, list) go to the configured
/// BaseUrl when set, otherwise to Twilio's default API host. Lookup calls always
/// go to Twilio's Lookup host, which BaseUrl does not govern.
/// Phone numbers and the auth token are never written to logs.
/// </summary>
public class TwilioSmsProvider : ISmsProvider, IPhoneNumberValidator
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string MessagingBaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? DefaultMessagingBaseUrl
        : _settings.BaseUrl!.TrimEnd('/');

    private string MessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string messageSid) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Twilio Lookup v2: GET https://lookups.twilio.com/v2/PhoneNumbers/{PhoneNumber}
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false };
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;

        var isValid = root.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        var canonical = root.TryGetProperty("phone_number", out var phoneProp) && phoneProp.ValueKind == JsonValueKind.String
            ? phoneProp.GetString()
            : null;
        var errors = root.TryGetProperty("validation_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array
            ? errorsProp.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList()
            : new List<string>();

        return new PhoneNumberValidationResult
        {
            IsValid = isValid && canonical is not null,
            CanonicalNumber = canonical,
            ValidationErrors = errors
        };
    }

    public async Task<SmsSendResult> SendMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        // Twilio Messages: POST /2010-04-01/Accounts/{AccountSid}/Messages.json
        // Scheduling: ScheduleType=fixed + SendAt (ISO-8601), requires a MessagingServiceSid.
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("MessagingServiceSid", _settings.MessagingServiceSid)
        };

        if (sendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await PostFormAsync(MessagesUrl, fields, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (errorCode, errorMessage) = ParseError(payload);
            _logger.LogWarning("Twilio rejected a message send: HTTP {StatusCode}, Twilio error {ErrorCode}", (int)response.StatusCode, errorCode);
            return new SmsSendResult { Accepted = false, ErrorCode = errorCode, ErrorMessage = errorMessage };
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsSendResult
        {
            Accepted = true,
            MessageSid = GetString(root, "sid"),
            Status = GetString(root, "status"),
            From = GetString(root, "from")
        };
    }

    public async Task<SmsMessageDetails?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageUrl(messageSid));
        using var response = await SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return ParseMessage(doc.RootElement);
    }

    public async Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Twilio: POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json with Status=canceled
        var fields = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await PostFormAsync(MessageUrl(messageSid), fields, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // e.g. Twilio error 21605 when the message has already been sent.
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (errorCode, _) = ParseError(payload);
            _logger.LogWarning("Twilio could not cancel message {MessageSid}: HTTP {StatusCode}, Twilio error {ErrorCode}",
                messageSid, (int)response.StatusCode, errorCode);
            return false;
        }

        return true;
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Twilio: POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json with Body set to an
        // empty string redacts the text permanently while keeping the message record.
        var fields = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await PostFormAsync(MessageUrl(messageSid), fields, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var (errorCode, _) = ParseError(payload);
            _logger.LogWarning("Twilio could not redact message {MessageSid}: HTTP {StatusCode}, Twilio error {ErrorCode}",
                messageSid, (int)response.StatusCode, errorCode);
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyList<SmsMessageDetails>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Twilio list filters: From, To, DateSent (YYYY-MM-DD, GMT, with >= / <= inequalities).
        // "DateSent<=d" means on or before midnight at the start of day d, so the upper bound
        // is the day after `to` to cover the whole range. The From filter is applied by the
        // provider so only this application's own sending number's traffic is returned.
        var fromDate = from.UtcDateTime.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var toDate = to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var url = MessagesUrl
            + "?From=" + Uri.EscapeDataString(_settings.FromNumber)
            + "&" + Uri.EscapeDataString("DateSent>=") + "=" + fromDate
            + "&" + Uri.EscapeDataString("DateSent<=") + "=" + toDate
            + "&PageSize=1000";

        var messages = new List<SmsMessageDetails>();

        while (url is not null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messagesProp) && messagesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messagesProp.EnumerateArray())
                {
                    messages.Add(ParseMessage(message));
                }
            }

            var nextPageUri = GetString(root, "next_page_uri");
            url = string.IsNullOrEmpty(nextPageUri) ? null : MessagingBaseUrl + nextPageUri;
        }

        // The provider's DateSent filter works at day granularity; narrow to the exact range here.
        return messages
            .Where(m => m.DateSent.HasValue && m.DateSent.Value >= from && m.DateSent.Value <= to)
            .ToList();
    }

    private static SmsMessageDetails ParseMessage(JsonElement element)
    {
        return new SmsMessageDetails
        {
            MessageSid = GetString(element, "sid") ?? string.Empty,
            Status = GetString(element, "status"),
            From = GetString(element, "from"),
            To = GetString(element, "to"),
            ErrorCode = GetString(element, "error_code"),
            DateCreated = ParseTwilioDate(GetString(element, "date_created")),
            DateSent = ParseTwilioDate(GetString(element, "date_sent"))
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Twilio renders dates in RFC 1123 form, e.g. "Mon, 29 Nov 2022 22:40:10 +0000".
        if (DateTimeOffset.TryParseExact(value, "r", CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            return exact;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static (string? Code, string? Message) ParseError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var code = root.TryGetProperty("code", out var codeProp) ? codeProp.ToString() : null;
            var message = GetString(root, "message");
            return (code, message);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private Task<HttpResponseMessage> PostFormAsync(string url, List<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        return SendAsync(request, cancellationToken);
    }

    private Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        return _httpClient.SendAsync(request, cancellationToken);
    }
}
