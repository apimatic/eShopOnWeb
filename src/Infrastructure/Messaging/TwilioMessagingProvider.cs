using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio implementation of <see cref="IMessagingProvider"/> over plain HTTPS.
/// Messaging API contract (verified against Twilio's official docs):
///   POST   {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json            send / schedule
///   GET    {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json      fetch status
///   POST   {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json      Status=canceled (cancel scheduled) / Body= (redact)
///   GET    {BaseUrl}/2010-04-01/Accounts/{AccountSid}/Messages.json?From=..&amp;DateSent&gt;=..&amp;DateSent&lt;=..  list
/// Lookup API (separate host, NOT governed by BaseUrl):
///   GET    https://lookups.twilio.com/v2/PhoneNumbers/{number}                 validate + canonical form
/// Auth: HTTP Basic with AccountSid:AuthToken. Credentials and phone numbers are never logged.
/// </summary>
public class TwilioMessagingProvider : IMessagingProvider
{
    private const string LookupBaseUrl = "https://lookups.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingProvider(HttpClient httpClient, IOptions<TwilioSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;

        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio settings are incomplete. Bind the 'Twilio' configuration section (AccountSid/AuthToken/FromNumber/MessagingServiceSid) from user-secrets or environment variables.");
        }

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
    }

    private string MessagesUrl => $"{_settings.EffectiveBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_settings.EffectiveBaseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        // Lookup API is served from its own host; Twilio:BaseUrl does not govern it.
        var url = $"{LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if ((int)response.StatusCode == 404)
        {
            return new PhoneNumberValidation(false, null, "The provider does not consider this a usable destination.");
        }
        await EnsureSuccessAsync(response, cancellationToken);

        var lookup = await response.Content.ReadFromJsonAsync<LookupResponse>(JsonOptions, cancellationToken);
        if (lookup == null || !lookup.Valid || string.IsNullOrEmpty(lookup.PhoneNumber))
        {
            var reason = lookup?.ValidationErrors is { Length: > 0 } errors ? string.Join(", ", errors) : "invalid number";
            return new PhoneNumberValidation(false, null, reason);
        }

        return new PhoneNumberValidation(true, lookup.PhoneNumber, null);
    }

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return await PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduled messages require a Messaging Service; From pins the sender to our configured number.
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return await PostMessageAsync(MessagesUrl, form, cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
    }

    public async Task<ProviderMessage?> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        if ((int)response.StatusCode == 404)
        {
            return null;
        }
        await EnsureSuccessAsync(response, cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<TwilioMessage>(JsonOptions, cancellationToken);
        return message?.ToProviderMessage();
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Updating Body to an empty string permanently disposes of the text while keeping the record.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        await PostMessageAsync(MessageUrl(messageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for our own sending number's messages only. The DateSent filters are
        // date-granular and would drop not-yet-sent scheduled messages (null DateSent), so the
        // precise [from, to] range is applied locally instead.
        var query = $"From={Uri.EscapeDataString(_settings.FromNumber)}&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? nextUrl = $"{MessagesUrl}?{query}";

        while (nextUrl != null)
        {
            using var response = await _httpClient.GetAsync(nextUrl, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioMessagePage>(JsonOptions, cancellationToken);
            if (page?.Messages != null)
            {
                results.AddRange(page.Messages.Select(m => m.ToProviderMessage()));
            }
            nextUrl = !string.IsNullOrEmpty(page?.NextPageUri)
                ? $"{_settings.EffectiveBaseUrl}{page.NextPageUri}"
                : null;
        }

        return results
            .Where(m =>
            {
                var when = m.DateSent ?? m.DateCreated;
                return when == null || (when >= from && when <= to);
            })
            .ToList();
    }

    private async Task<ProviderMessage> PostMessageAsync(string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<TwilioMessage>(JsonOptions, cancellationToken);
        return message?.ToProviderMessage()
            ?? throw new InvalidOperationException("The provider returned an empty message resource.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? errorCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioError>(JsonOptions, cancellationToken);
            errorCode = error?.Code;
        }
        catch (JsonException)
        {
            // Non-JSON error body; fall through to the generic exception below.
        }

        // Never include the response body in the exception: it can contain destination numbers.
        throw new TwilioApiException((int)response.StatusCode, errorCode);
    }

    private sealed class TwilioApiException : Exception
    {
        public TwilioApiException(int statusCode, int? providerErrorCode)
            : base($"Twilio API request failed with HTTP {statusCode}{(providerErrorCode.HasValue ? $" (Twilio error {providerErrorCode.Value})" : string.Empty)}.")
        {
            StatusCode = statusCode;
            ProviderErrorCode = providerErrorCode;
        }

        public int StatusCode { get; }
        public int? ProviderErrorCode { get; }
    }

    private sealed class LookupResponse
    {
        [JsonPropertyName("phone_number")] public string? PhoneNumber { get; set; }
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        [JsonPropertyName("validation_errors")] public string[]? ValidationErrors { get; set; }
    }

    private sealed class TwilioMessage
    {
        [JsonPropertyName("sid")] public string Sid { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }

        public ProviderMessage ToProviderMessage() => new(
            Sid, Status, To, From, Body, ParseTwilioDate(DateCreated), ParseTwilioDate(DateSent), ErrorCode, ErrorMessage);

        private static DateTimeOffset? ParseTwilioDate(string? value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
    }

    private sealed class TwilioMessagePage
    {
        [JsonPropertyName("messages")] public List<TwilioMessage>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
    }
}
