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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio messaging + lookup client over plain HTTP.
/// Verified against https://www.twilio.com/docs/messaging/api/message-resource
/// and https://www.twilio.com/docs/lookup/v2-api.
/// The auth token is used only for the Basic auth header and is never logged.
/// Destination numbers are never logged either.
/// </summary>
public class TwilioClient : IMessageProvider, IPhoneNumberValidator
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioClient(HttpClient httpClient, IOptions<TwilioSettings> settings, IAppLogger<TwilioClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio settings are incomplete: 'Twilio:AccountSid' and 'Twilio:AuthToken' must be configured (e.g. via user-secrets).");
        }

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    private string AccountMessagesUrl => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup is served from its own host; Twilio:BaseUrl does not govern it.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (json is null)
        {
            return new PhoneNumberValidationResult { IsValid = false, ValidationErrors = new[] { "VALIDATION_UNAVAILABLE" } };
        }

        var result = new PhoneNumberValidationResult
        {
            IsValid = json.RootElement.TryGetProperty("valid", out var valid) && valid.GetBoolean(),
            CanonicalNumber = json.RootElement.TryGetProperty("phone_number", out var number) && number.ValueKind == JsonValueKind.String
                ? number.GetString()
                : null
        };

        if (json.RootElement.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
        {
            result.ValidationErrors = errors.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .ToList();
        }

        return result;
    }

    public Task<ProviderMessageResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(AccountMessagesUrl, form, cancellationToken);
    }

    public Task<ProviderMessageResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling is a Messaging-Service-only feature: ScheduleType=fixed + SendAt (ISO 8601).
        // From is passed explicitly so the message still goes out from this application's
        // configured sending number (reconciliation depends on it).
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
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
        if (json is null)
        {
            return null;
        }

        return new ProviderMessageResult
        {
            Accepted = true,
            MessageSid = GetString(json.RootElement, "sid"),
            Status = GetString(json.RootElement, "status"),
            ErrorCode = GetString(json.RootElement, "error_code"),
            ErrorMessage = GetString(json.RootElement, "error_message")
        };
    }

    public async Task<bool> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Updating Status to "canceled" is the documented way to call off a scheduled message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // POSTing an empty Body redacts the message text while keeping the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only,
        // rather than filtering a wider answer after the fact.
        var records = new List<ProviderMessageRecord>();
        var fromParam = Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var toParam = Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var url = $"{AccountMessagesUrl}?From={Uri.EscapeDataString(_settings.FromNumber)}&DateSent%3E={fromParam}&DateSent%3C={toParam}&PageSize=1000";

        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var json = await ReadJsonAsync(response, cancellationToken);
            if (json is null)
            {
                break;
            }

            if (json.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var message in messages.EnumerateArray())
                {
                    records.Add(new ProviderMessageRecord
                    {
                        MessageSid = GetString(message, "sid") ?? string.Empty,
                        To = GetString(message, "to") ?? string.Empty,
                        From = GetString(message, "from") ?? string.Empty,
                        Status = GetString(message, "status") ?? string.Empty,
                        ErrorCode = GetString(message, "error_code"),
                        ErrorMessage = GetString(message, "error_message"),
                        DateSent = GetDate(message, "date_sent"),
                        DateCreated = GetDate(message, "date_created")
                    });
                }
            }

            // Page through the whole range.
            var nextPageUri = GetString(json.RootElement, "next_page_uri");
            url = string.IsNullOrEmpty(nextPageUri) ? null : _messagingBaseUrl + nextPageUri;
        }

        return records;
    }

    private async Task<ProviderMessageResult> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        var json = await ReadJsonAsync(response, cancellationToken);

        if (response.IsSuccessStatusCode && json is not null)
        {
            return new ProviderMessageResult
            {
                Accepted = true,
                MessageSid = GetString(json.RootElement, "sid"),
                Status = GetString(json.RootElement, "status"),
                ErrorCode = GetString(json.RootElement, "error_code"),
                ErrorMessage = GetString(json.RootElement, "error_message")
            };
        }

        // Provider-side rejection (e.g. unusable destination): an outcome, not an exception.
        // The provider's error payload is returned to the caller but never logged here,
        // since it may quote the destination number.
        _logger.LogWarning($"Twilio rejected a messaging request with HTTP {(int)response.StatusCode}.");
        return new ProviderMessageResult
        {
            Accepted = false,
            Status = "failed",
            ErrorCode = json is not null ? GetString(json.RootElement, "code") : ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
            ErrorMessage = json is not null ? GetString(json.RootElement, "message") : response.ReasonPhrase
        };
    }

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

    private static string? GetString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        var raw = GetString(element, property);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Twilio renders dates as RFC 2822 (e.g. "Wed, 31 Aug 2016 14:00:00 +0000").
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
