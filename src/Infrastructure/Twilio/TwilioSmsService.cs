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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio messaging + lookup client. All messaging-API traffic goes through the
/// configured base address (default https://api.twilio.com); number validation
/// uses the Lookup API on its own host. The auth token is used only to build
/// the Basic authorization header and is never logged.
/// </summary>
public class TwilioSmsService : ISmsService, IPhoneNumberValidator
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("Body", body)
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages for future delivery.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", _settings.FromNumber),
            new("MessagingServiceSid", _settings.MessagingServiceSid),
            new("Body", body),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))
        };
        return await PostMessageAsync(form, cancellationToken);
    }

    public async Task<SmsMessageState?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await SendAuthedAsync(HttpMethod.Get, MessageUri(messageSid), cancellationToken: cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new SmsMessageState(
            root.GetProperty("sid").GetString() ?? messageSid,
            root.GetProperty("status").GetString() ?? "unknown",
            GetNullableInt(root, "error_code"),
            ParseRfc2822(GetNullableString(root, "date_sent")),
            ParseRfc2822(GetNullableString(root, "date_created")));
    }

    public async Task<bool> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await SendAuthedAsync(HttpMethod.Post, MessageUri(messageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio refused to cancel message (HTTP {StatusCode}).", (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<bool> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // An empty Body redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await SendAuthedAsync(HttpMethod.Post, MessageUri(messageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio refused to redact message body (HTTP {StatusCode}).", (int)response.StatusCode);
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<SmsMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number only, rather
        // than filtering a wider answer after the fact. The provider filters
        // DateSent at day granularity (GMT), so widen by a day on each side and
        // then bound precisely to the requested range locally.
        var query = string.Join("&", new[]
        {
            "From=" + Uri.EscapeDataString(_settings.FromNumber),
            "DateSent%3E=" + from.UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "DateSent%3C=" + to.UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            "PageSize=1000"
        });

        var results = new List<SmsMessageRecord>();
        string? nextUri = $"{MessagesUri()}?{query}";

        while (nextUri is not null)
        {
            using var response = await SendAuthedAsync(HttpMethod.Get, nextUri, cancellationToken: cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            foreach (var message in root.GetProperty("messages").EnumerateArray())
            {
                var dateSent = ParseRfc2822(GetNullableString(message, "date_sent"));
                var dateCreated = ParseRfc2822(GetNullableString(message, "date_created"));
                var effectiveDate = dateSent ?? dateCreated;
                if (effectiveDate is null || effectiveDate < from || effectiveDate > to)
                {
                    continue;
                }

                results.Add(new SmsMessageRecord(
                    message.GetProperty("sid").GetString() ?? string.Empty,
                    GetNullableString(message, "to"),
                    GetNullableString(message, "from"),
                    message.GetProperty("status").GetString() ?? "unknown",
                    GetNullableInt(message, "error_code"),
                    dateSent,
                    dateCreated));
            }

            var nextPageUri = GetNullableString(root, "next_page_uri");
            nextUri = nextPageUri is null ? null : _settings.MessagingBaseUrl + nextPageUri;
        }

        return results;
    }

    public async Task<PhoneNumberValidationResult> ValidateAsync(string rawNumber, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        // No Fields parameter: the formatting/validation call is free.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(rawNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += "?CountryCode=" + Uri.EscapeDataString(countryCode);
        }

        using var response = await SendAuthedAsync(HttpMethod.Get, url, cancellationToken: cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return PhoneNumberValidationResult.Invalid(new[] { "NOT_A_NUMBER" });
        }
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validProp) && validProp.GetBoolean();
        if (!valid)
        {
            var errors = root.TryGetProperty("validation_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array
                ? errorsProp.EnumerateArray().Select(e => e.GetString() ?? "UNKNOWN").ToArray()
                : new[] { "INVALID" };
            return PhoneNumberValidationResult.Invalid(errors);
        }

        return new PhoneNumberValidationResult(
            true,
            GetNullableString(root, "phone_number"),
            GetNullableString(root, "national_format"),
            Array.Empty<string>());
    }

    private async Task<SmsSendResult> PostMessageAsync(List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var response = await SendAuthedAsync(HttpMethod.Post, MessagesUri(), form, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The provider rejected the message outright (e.g. unusable destination).
            // That is an outcome, not an exception: record it and let the operation succeed.
            var (code, _) = TryParseError(payload);
            _logger.LogWarning("Twilio rejected a message at send time (HTTP {StatusCode}, error {ErrorCode}).", (int)response.StatusCode, code);
            return new SmsSendResult(null, "failed", code);
        }

        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        return new SmsSendResult(
            GetNullableString(root, "sid"),
            root.GetProperty("status").GetString() ?? "queued",
            GetNullableInt(root, "error_code"));
    }

    private string MessagesUri() => $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUri(string sid) => $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private async Task<HttpResponseMessage> SendAuthedAsync(HttpMethod method, string uri, List<KeyValuePair<string, string>>? form = null, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException("Twilio settings are not configured (Twilio:AccountSid / Twilio:AuthToken / Twilio:FromNumber).");
        }

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }
        return await _httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // Deliberately do not surface the provider's error message: it can embed
        // the destination number, which must never reach the logs.
        var (code, _) = TryParseError(await response.Content.ReadAsStringAsync(cancellationToken));
        throw new HttpRequestException($"Twilio request failed with HTTP {(int)response.StatusCode} (error {code?.ToString() ?? "unknown"}).");
    }

    private static (int? Code, string? Message) TryParseError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            return (GetNullableInt(root, "code"), GetNullableString(root, "message"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string? GetNullableString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetNullableInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var prop))
        {
            return null;
        }
        return prop.ValueKind switch
        {
            JsonValueKind.Number when prop.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(prop.GetString(), out var value) => value,
            _ => null
        };
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
