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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/> over plain HTTPS.
///
/// Verified against Twilio's official documentation:
/// - Lookup v2:    GET https://lookups.twilio.com/v2/PhoneNumbers/{number} -> valid / phone_number (E.164) / validation_errors
/// - Send:         POST /2010-04-01/Accounts/{sid}/Messages.json (To, From, Body)
/// - Schedule:     same POST with MessagingServiceSid, ScheduleType=fixed, SendAt (ISO-8601, 15 min - 35 days out)
/// - Cancel:       POST /2010-04-01/Accounts/{sid}/Messages/{sid}.json with Status=canceled
/// - Fetch:        GET  /2010-04-01/Accounts/{sid}/Messages/{sid}.json
/// - Redact body:  POST /2010-04-01/Accounts/{sid}/Messages/{sid}.json with Body="" (record survives)
/// - List:         GET  /2010-04-01/Accounts/{sid}/Messages.json?From=...&DateSent>=...&DateSent&lt;=... with next_page_uri paging
///
/// Destination numbers and the auth token are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
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

        var authBytes = Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authBytes));
    }

    private string MessagingBaseUrl =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultMessagingBaseUrl : _settings.BaseUrl!.TrimEnd('/');

    private string AccountMessagesUrl => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup is served from its own host; Twilio:BaseUrl does not govern it.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var (code, message) = ReadTwilioError(json);
            _logger.LogWarning("Twilio Lookup rejected a number validation request: {Code} {Message}", code, message);
            return new PhoneNumberValidation(false, null, new[] { message ?? "The provider rejected the lookup request." });
        }

        var isValid = json?.RootElement.TryGetProperty("valid", out var valid) == true && valid.GetBoolean();
        var canonical = json?.RootElement.TryGetProperty("phone_number", out var pn) == true ? pn.GetString() : null;
        var errors = new List<string>();
        if (json?.RootElement.TryGetProperty("validation_errors", out var ve) == true && ve.ValueKind == JsonValueKind.Array)
        {
            errors.AddRange(ve.EnumerateArray().Select(e => e.GetString() ?? string.Empty).Where(s => s.Length > 0));
        }
        return new PhoneNumberValidation(isValid, isValid ? canonical : null, errors);
    }

    public Task<ProviderMessageResult> SendMessageAsync(string toCanonicalNumber, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = toCanonicalNumber,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(AccountMessagesUrl, form, cancellationToken);
    }

    public Task<ProviderMessageResult> ScheduleMessageAsync(string toCanonicalNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Twilio requires a Messaging Service for scheduled messages (error 35118 otherwise).
        var form = new Dictionary<string, string>
        {
            ["To"] = toCanonicalNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync(AccountMessagesUrl, form, cancellationToken);
    }

    public async Task<ProviderMessageResult?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        var json = await ReadJsonAsync(response, cancellationToken);
        return ReadMessageResult(json, response.IsSuccessStatusCode);
    }

    public async Task<ProviderMessageResult?> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await PostFormAsync(MessageUrl(messageSid), form, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);
        return ReadMessageResult(json, response.IsSuccessStatusCode);
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // POSTing an empty Body redacts the text at the provider but keeps the message record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync(MessageUrl(messageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var json = await ReadJsonAsync(response, cancellationToken);
            var (code, message) = ReadTwilioError(json);
            _logger.LogWarning("Twilio body redaction failed for message {MessageSid}: {Code} {Message}", messageSid, code, message);
            return false;
        }
        return true;
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's messages (server-side filter).
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_settings.FromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}",
            $"DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessageRecord>();
        string? nextUrl = $"{AccountMessagesUrl}?{query}";
        while (nextUrl is not null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            var json = await ReadJsonAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode || json is null)
            {
                var (code, message) = ReadTwilioError(json);
                throw new InvalidOperationException($"Twilio message listing failed: {code} {message}");
            }

            if (json.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in messages.EnumerateArray())
                {
                    results.Add(new ProviderMessageRecord(
                        m.GetProperty("sid").GetString()!,
                        GetString(m, "status"),
                        GetString(m, "error_code"),
                        GetString(m, "error_message"),
                        GetDate(m, "date_sent"),
                        GetDate(m, "date_created")));
                }
            }

            nextUrl = json.RootElement.TryGetProperty("next_page_uri", out var next) && next.ValueKind == JsonValueKind.String
                ? MessagingBaseUrl + next.GetString()
                : null;
        }
        return results;
    }

    private async Task<ProviderMessageResult> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await PostFormAsync(url, form, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);
        return ReadMessageResult(json, response.IsSuccessStatusCode);
    }

    private Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
        => _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);

    private static async Task<JsonDocument?> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }
        try
        {
            return JsonDocument.Parse(content);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ProviderMessageResult ReadMessageResult(JsonDocument? json, bool httpSuccess)
    {
        if (json is null)
        {
            return new ProviderMessageResult(false, null, null, null, httpSuccess ? null : "Empty provider response.", null);
        }

        var root = json.RootElement;
        if (!httpSuccess)
        {
            var (code, message) = ReadTwilioError(json);
            return new ProviderMessageResult(false, GetString(root, "sid"), GetString(root, "status"), code, message, null);
        }

        return new ProviderMessageResult(
            true,
            GetString(root, "sid"),
            GetString(root, "status"),
            GetString(root, "error_code"),
            GetString(root, "error_message"),
            GetDate(root, "date_sent"));
    }

    private static (string? Code, string? Message) ReadTwilioError(JsonDocument? json)
    {
        if (json is null)
        {
            return (null, null);
        }
        var root = json.RootElement;
        string? code = null;
        if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number)
        {
            code = c.GetInt32().ToString(CultureInfo.InvariantCulture);
        }
        return (code, GetString(root, "message"));
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return value.GetString();
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        // Twilio renders dates as RFC 2822, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        var text = GetString(element, property);
        if (text is null)
        {
            return null;
        }
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
