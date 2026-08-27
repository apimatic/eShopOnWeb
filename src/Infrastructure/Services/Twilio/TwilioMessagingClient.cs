using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Twilio messaging API client (api.twilio.com, /2010-04-01). Talks form-encoded bodies and
/// snake_case JSON responses per the provider's API reference. The base address is taken
/// verbatim from Twilio:BaseUrl when configured. Never logs phone numbers, bodies or credentials.
/// </summary>
public class TwilioMessagingClient : ISmsMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private string MessagesUrl => $"{_options.EffectiveBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"{_options.EffectiveBaseUrl}/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    public async Task<SmsMessageResult> SendMessageAsync(string toNumber, string body, DateTimeOffset? sendAtUtc = null, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var form = new List<KeyValuePair<string, string>>
        {
            new("To", toNumber),
            new("Body", body),
            new("From", _options.FromNumber),
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }

        if (sendAtUtc.HasValue)
        {
            // Provider-side scheduling: only available through a Messaging Service.
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule messages with the provider.");
            }
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        var message = await PostFormAsync(MessagesUrl, form, cancellationToken);
        _logger.LogInformation("Message {MessageSid} accepted by provider with status {Status}", message.Sid, message.Status);
        return ToResult(message);
    }

    public async Task<SmsMessageResult> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        using var response = await _httpClient.GetAsync(MessageUrl(providerMessageSid), cancellationToken);
        var message = await ReadBodyAsync<TwilioMessage>(response, cancellationToken);
        return ToResult(message);
    }

    public async Task<SmsMessageResult> CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        var message = await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        _logger.LogInformation("Message {MessageSid} cancelled at provider, status {Status}", message.Sid, message.Status);
        return ToResult(message);
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        // An empty Body redacts the message text at the provider.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        await PostFormAsync(MessageUrl(providerMessageSid), form, cancellationToken);
        _logger.LogInformation("Message {MessageSid} body redacted at provider", providerMessageSid);
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        _options.Validate();

        // Ask the provider for only this application's sending number, and only the requested
        // sent-date window (UTC dates, comparison encoded in the parameter name).
        var url = MessagesUrl +
            $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={from.UtcDateTime:yyyy-MM-dd}" +
            $"&DateSent%3C={to.UtcDateTime:yyyy-MM-dd}" +
            "&PageSize=1000";

        var results = new List<SmsMessageResult>();
        while (url is not null)
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            var page = await ReadBodyAsync<TwilioMessageListPage>(response, cancellationToken);
            if (page.Messages is not null)
            {
                results.AddRange(page.Messages.Select(ToResult));
            }
            url = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : _options.EffectiveBaseUrl + page.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessage> PostFormAsync(string url, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        return await ReadBodyAsync<TwilioMessage>(response, cancellationToken);
    }

    private static async Task<T> ReadBodyAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            TwilioError? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<TwilioError>(cancellationToken: cancellationToken);
            }
            catch
            {
                // Non-JSON error body; fall through to the generic exception below.
            }
            throw new TwilioApiException((int)response.StatusCode, error?.Code, error?.Message ?? response.ReasonPhrase ?? "unknown error");
        }

        var body = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        return body ?? throw new TwilioApiException((int)response.StatusCode, null, "Provider returned an empty response body.");
    }

    private static SmsMessageResult ToResult(TwilioMessage message) => new(
        message.Sid ?? string.Empty,
        message.Status ?? string.Empty,
        message.ErrorCode?.ToString(CultureInfo.InvariantCulture),
        message.ErrorMessage,
        ParseRfc2822(message.DateSent),
        ParseRfc2822(message.DateCreated));

    private static DateTimeOffset? ParseRfc2822(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private sealed class TwilioMessage
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
    }

    private sealed class TwilioMessageListPage
    {
        [JsonPropertyName("messages")] public List<TwilioMessage>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class TwilioError
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("status")] public int Status { get; set; }
    }
}
