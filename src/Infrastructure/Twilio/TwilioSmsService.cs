using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio messaging provider. Talks to the REST API directly over HTTP Basic auth
/// (AccountSid:AuthToken). The messaging API base address is <see cref="TwilioSettings.BaseUrl"/>
/// when set, otherwise the provider default; the Lookup API always uses its own host.
/// Phone numbers and the auth token are never logged.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioSmsService(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _settings.Validate();

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode, CancellationToken cancellationToken = default)
    {
        // Lookup API: GET /v2/PhoneNumbers/{PhoneNumber} — no Fields parameter means the free
        // formatting/validation call. The '+' must be percent-encoded in the path.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            url += $"?CountryCode={Uri.EscapeDataString(countryCode)}";
        }

        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return PhoneNumberValidationResult.Invalid("The phone number is not valid.");
        }
        response.EnsureSuccessStatusCode();

        var payload = await DeserializeAsync<LookupPhoneNumberResponse>(response, cancellationToken);
        if (payload == null || !payload.Valid || string.IsNullOrEmpty(payload.PhoneNumber))
        {
            var errors = payload?.ValidationErrors != null ? string.Join(", ", payload.ValidationErrors) : null;
            return PhoneNumberValidationResult.Invalid(errors ?? "The phone number is not valid.");
        }

        return PhoneNumberValidationResult.Valid(payload.PhoneNumber, payload.NationalFormat);
    }

    public Task<SmsSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber!,
            ["Body"] = body
        };
        AddMessagingService(form);

        return PostMessageAsync(form, cancellationToken);
    }

    public Task<SmsSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Scheduling messages with the provider requires Twilio:MessagingServiceSid to be configured.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber!,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        AddMessagingService(form);

        return PostMessageAsync(form, cancellationToken);
    }

    public async Task<SmsMessageState?> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var payload = await DeserializeAsync<MessageResponse>(response, cancellationToken);
        return payload == null ? null : ToState(payload);
    }

    public async Task<bool> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating Status to "canceled" calls off a not-yet-sent scheduled message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var response = await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var payload = await DeserializeAsync<MessageResponse>(response, cancellationToken);
        return payload != null && string.Equals(payload.Status, "canceled", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating Body to an empty string redacts the message text at the provider.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var response = await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<IReadOnlyList<SmsMessageState>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only,
        // bounded by the range's dates (the provider's DateSent filters are date-granular).
        var query = new Dictionary<string, string>
        {
            ["From"] = _settings.FromNumber!,
            ["DateSent>="] = from.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["DateSent<="] = to.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };

        var results = new List<SmsMessageState>();
        string? nextUrl = $"{MessagesUrl()}?{BuildQuery(query)}";

        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            var page = await DeserializeAsync<MessageListPage>(response, cancellationToken);
            if (page?.Messages == null)
            {
                break;
            }

            results.AddRange(page.Messages.Select(ToState));
            nextUrl = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : new Uri(new Uri(_settings.MessagingBaseUrl + "/"), page.NextPageUri).ToString();
        }

        // Refine the date-granular provider filter to the exact requested instants.
        return results
            .Where(m =>
            {
                var instant = m.DateSent ?? m.DateCreated;
                return instant == null || (instant >= from && instant <= to);
            })
            .ToList();
    }

    private async Task<SmsSendResult> PostMessageAsync(Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var response = await PostFormAsync(MessagesUrl(), form, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var payload = await DeserializeAsync<MessageResponse>(response, cancellationToken);
            return payload?.Sid != null
                ? SmsSendResult.Success(payload.Sid, payload.Status)
                : SmsSendResult.Failure("The provider accepted the request but returned no message identifier.");
        }

        var error = await DeserializeAsync<ErrorResponse>(response, cancellationToken);
        return SmsSendResult.Failure(error?.Message ?? $"The provider rejected the message (HTTP {(int)response.StatusCode}).", error?.Code);
    }

    private void AddMessagingService(Dictionary<string, string> form)
    {
        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form["MessagingServiceSid"] = _settings.MessagingServiceSid!;
        }
    }

    private string MessagesUrl() => $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageUrl(string sid) => $"{_settings.MessagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private async Task<HttpResponseMessage> PostFormAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        return await _httpClient.PostAsync(url, content, cancellationToken);
    }

    private static string BuildQuery(Dictionary<string, string> parameters) =>
        string.Join("&", parameters.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static SmsMessageState ToState(MessageResponse message) => new()
    {
        ProviderMessageSid = message.Sid ?? string.Empty,
        Status = message.Status,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        From = message.From,
        To = message.To,
        DateCreated = ParseRfc2822(message.DateCreated),
        DateSent = ParseRfc2822(message.DateSent),
        DateUpdated = ParseRfc2822(message.DateUpdated)
    };

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private class LookupPhoneNumberResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("national_format")] public string? NationalFormat { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private class MessageResponse
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("date_updated")] public string? DateUpdated { get; set; }
    }

    private class MessageListPage
    {
        [JsonPropertyName("messages")] public List<MessageResponse>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private class ErrorResponse
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }
}
