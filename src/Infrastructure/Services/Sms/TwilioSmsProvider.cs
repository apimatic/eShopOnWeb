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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsProvider"/> over plain HTTPS.
/// Messaging API calls go to Twilio:BaseUrl when set, otherwise api.twilio.com.
/// The Lookup API is a separate Twilio capability served from lookups.twilio.com and
/// is not governed by the BaseUrl override.
/// Destination numbers, message bodies and credentials are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioSmsProvider(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl!.TrimEnd('/');

        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PhoneNumberValidationResult { IsValid = false, Error = "number not found" };
        }

        await EnsureSuccess(response, cancellationToken);

        var lookup = await ReadJson<TwilioLookupResponse>(response, cancellationToken);
        if (lookup?.Valid == true && !string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            return new PhoneNumberValidationResult { IsValid = true, CanonicalNumber = lookup.PhoneNumber };
        }

        var errors = lookup?.ValidationErrors != null ? string.Join(", ", lookup.ValidationErrors) : "invalid number";
        return new PhoneNumberValidationResult { IsValid = false, Error = errors };
    }

    public Task<ProviderSendResult> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return PostMessageAsync(MessagesUrl(), form, cancellationToken);
    }

    public Task<ProviderSendResult> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            return Task.FromResult(ProviderSendResult.Fail("Twilio:MessagingServiceSid is required to schedule messages"));
        }

        // Twilio message scheduling: ScheduleType=fixed with an ISO-8601 UTC SendAt
        // (15 minutes to 35 days ahead); scheduling requires a Messaging Service.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid!,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return PostMessageAsync(MessagesUrl(), form, cancellationToken);
    }

    public Task<ProviderSendResult> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return PostMessageWithRetryAsync(MessageUrl(messageSid), form, cancellationToken);
    }

    public async Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccess(response, cancellationToken);
        var message = await ReadJson<TwilioMessageResource>(response, cancellationToken);
        return message?.ToProviderMessage();
    }

    public async Task<bool> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Updating a message with an empty Body erases its content at the provider
        // while keeping the message record itself.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        var result = await PostMessageWithRetryAsync(MessageUrl(messageSid), form, cancellationToken);
        return result.Success;
    }

    private async Task<ProviderSendResult> PostMessageWithRetryAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        // A message resource can briefly 404 right after creation while the provider
        // replicates it; retry updates a few times before giving up.
        for (var attempt = 0; ; attempt++)
        {
            var result = await PostMessageAsync(url, form, cancellationToken);
            if (result.Success || !result.NotFound || attempt >= 2)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(1.5 * (attempt + 1)), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesFromSendingNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only.
        // DateSentAfter/DateSentBefore are GMT date-granular and inclusive; the precise
        // instant filter is applied below. (Two inequalities on DateSent itself cannot
        // be combined in a single Twilio list request.)
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSentAfter={from.UtcDateTime:yyyy-MM-dd}" +
                    $"&DateSentBefore={to.UtcDateTime:yyyy-MM-dd}" +
                    "&PageSize=1000";

        var messages = new List<ProviderMessage>();
        string? nextUri = MessagesUrl() + query;

        while (nextUri != null)
        {
            var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            await EnsureSuccess(response, cancellationToken);

            var page = await ReadJson<TwilioMessageListPage>(response, cancellationToken);
            if (page?.Messages != null)
            {
                messages.AddRange(page.Messages.Select(m => m.ToProviderMessage()));
            }

            nextUri = string.IsNullOrEmpty(page?.NextPageUri) ? null : _messagingBaseUrl + page!.NextPageUri;
        }

        return messages
            .Where(m =>
            {
                var instant = m.DateSent ?? m.DateCreated;
                return instant == null || (instant >= from && instant <= to);
            })
            .ToList();
    }

    private string MessagesUrl() => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_messagingBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private async Task<ProviderSendResult> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        var response = await _httpClient.PostAsync(url, new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadJson<TwilioErrorResponse>(response, cancellationToken);
            _logger.LogWarning("Twilio rejected a messaging request: {StatusCode} code {TwilioCode}: {Message}",
                (int)response.StatusCode, error?.Code, error?.Message);
            return ProviderSendResult.Fail(error?.Message ?? $"provider returned {(int)response.StatusCode}",
                notFound: response.StatusCode == HttpStatusCode.NotFound);
        }

        var message = await ReadJson<TwilioMessageResource>(response, cancellationToken);
        if (message?.Sid == null)
        {
            return ProviderSendResult.Fail("provider response did not include a message identifier");
        }

        return ProviderSendResult.Ok(message.ToProviderMessage());
    }

    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadJson<TwilioErrorResponse>(response, cancellationToken);
            throw new HttpRequestException($"Twilio request failed with {(int)response.StatusCode}: {error?.Message}");
        }
    }

    private static async Task<T?> ReadJson<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content);
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // Twilio timestamps are RFC 2822, e.g. "Fri, 24 May 2019 17:44:46 +0000".
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }

    private class TwilioLookupResponse
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("validation_errors")] public List<string>? ValidationErrors { get; set; }
    }

    private class TwilioErrorResponse
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
    }

    private class TwilioMessageListPage
    {
        [JsonPropertyName("messages")] public List<TwilioMessageResource>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private class TwilioMessageResource
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }

        public ProviderMessage ToProviderMessage() => new()
        {
            Sid = Sid ?? string.Empty,
            Status = Status,
            From = From,
            To = To,
            Body = Body,
            DateCreated = ParseTwilioDate(DateCreated),
            DateSent = ParseTwilioDate(DateSent),
            ErrorCode = ErrorCode,
            ErrorMessage = ErrorMessage
        };
    }
}
