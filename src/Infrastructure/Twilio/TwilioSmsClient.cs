using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio implementation of <see cref="ISmsNotificationClient"/>.
/// Messaging calls go to the configured BaseUrl (default https://api.twilio.com);
/// number validation uses the Lookup API on its own host (https://lookups.twilio.com),
/// which BaseUrl does not govern. Never logs phone numbers or credentials.
/// </summary>
public class TwilioSmsClient : ISmsNotificationClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsClient> _logger;

    public TwilioSmsClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

    private string MessagingUrl(string relativePath) =>
        $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/{relativePath}";

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if ((int)response.StatusCode == 404)
        {
            return new PhoneNumberValidationResult(false, null, null, new[] { "NOT_A_NUMBER" });
        }
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        var valid = root.TryGetProperty("valid", out var validProp) && validProp.ValueKind == JsonValueKind.True;
        var e164 = GetString(root, "phone_number");
        var national = GetString(root, "national_format");
        var errors = root.TryGetProperty("validation_errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Array
            ? errorsProp.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0).ToList()
            : new List<string>();

        return new PhoneNumberValidationResult(valid, valid ? e164 : null, valid ? national : null, errors);
    }

    public Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(MessagingUrl("Messages.json"), form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages with the provider.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync(MessagingUrl("Messages.json"), form, cancellationToken);
    }

    public async Task<SmsSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };

        for (var attempt = 0; ; attempt++)
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _httpClient.PostAsync(MessagingUrl($"Messages/{messageSid}.json"), content, cancellationToken);

            // A freshly created message can briefly 404 on update before it is fully addressable
            if ((int)response.StatusCode == 404 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await EnsureSuccessAsync(response, cancellationToken);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;
            return new SmsSendResult(
                GetString(root, "sid"),
                GetString(root, "status") ?? string.Empty,
                GetInt(root, "error_code"),
                GetString(root, "error_message"));
        }
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };

        for (var attempt = 0; ; attempt++)
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _httpClient.PostAsync(MessagingUrl($"Messages/{messageSid}.json"), content, cancellationToken);

            // A freshly created message can briefly 404 on update before it is fully addressable
            if ((int)response.StatusCode == 404 && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            await EnsureSuccessAsync(response, cancellationToken);
            return;
        }
    }

    public async Task<SmsMessageDetails> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var response = await _httpClient.GetAsync(MessagingUrl($"Messages/{messageSid}.json"), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new SmsMessageDetails(
            GetString(root, "sid") ?? messageSid,
            GetString(root, "status") ?? string.Empty,
            GetInt(root, "error_code"),
            GetString(root, "error_message"),
            GetDate(root, "date_sent"));
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        // Ask the provider for only this application's own sending number. DateSent</> are
        // date-granular GMT and exclusive of the given date, so widen by a day on each side;
        // the exact date-time range is applied below.
        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={from.UtcDateTime.Date.AddDays(-1):yyyy-MM-dd}" +
                    $"&DateSent%3C={to.UtcDateTime.Date.AddDays(1):yyyy-MM-dd}" +
                    "&PageSize=1000";

        var results = new List<ProviderMessageRecord>();
        string? nextUri = $"{MessagingUrl("Messages.json")}?{query}";

        while (nextUri is not null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            if (root.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    var dateSent = GetDate(message, "date_sent");
                    var dateCreated = GetDate(message, "date_created");
                    var effectiveDate = dateSent ?? dateCreated;
                    if (effectiveDate is null || effectiveDate < from || effectiveDate > to)
                    {
                        continue;
                    }

                    results.Add(new ProviderMessageRecord(
                        GetString(message, "sid") ?? string.Empty,
                        GetString(message, "to"),
                        GetString(message, "status") ?? string.Empty,
                        GetInt(message, "error_code"),
                        dateSent,
                        dateCreated));
                }
            }

            var nextPageUri = GetString(root, "next_page_uri");
            nextUri = string.IsNullOrEmpty(nextPageUri) ? null : MessagingBaseUrl + nextPageUri;
        }

        return results;
    }

    private async Task<SmsSendResult> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;
        return new SmsSendResult(
            GetString(root, "sid"),
            GetString(root, "status") ?? string.Empty,
            GetInt(root, "error_code"),
            GetString(root, "error_message"));
    }

    private void EnsureConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new InvalidOperationException("Twilio settings are not configured (Twilio:AccountSid, Twilio:AuthToken, Twilio:FromNumber).");
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? code = null;
        var message = $"Twilio API returned {(int)response.StatusCode}.";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            code = GetInt(doc.RootElement, "code");
            message = GetString(doc.RootElement, "message") ?? message;
        }
        catch (JsonException)
        {
        }

        throw new TwilioApiException(code, message, (int)response.StatusCode);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static int? GetInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
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

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var value = GetString(element, property);
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        // Twilio returns RFC 2822 timestamps (e.g. "Wed, 01 Apr 2026 12:00:00 +0000")
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
