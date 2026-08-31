using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against the authoritative
/// OpenAPI specification (api-specs/twilio/twilio_api_v2010):
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages.json          (CreateMessage)
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages.json          (ListMessage)
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json    (FetchMessage)
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json    (UpdateMessage)
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// The auth token is never logged.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultBaseUrl = "https://api.twilio.com";
    private const string DateSentFilterFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException("Twilio:AccountSid and Twilio:AuthToken must be configured.");
        }

        _httpClient = httpClient;
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? DefaultBaseUrl : _options.BaseUrl!;
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}")));
    }

    public async Task<TwilioMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            form.Add(new("From", _options.FromNumber));
        }

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            // Message scheduling: ScheduleType=fixed with an ISO 8601 SendAt (Messaging Services only).
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture)));
        }

        using var response = await _httpClient.PostAsync(MessagesUri(), new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUri(messageSid), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var response = await _httpClient.PostAsync(MessageUri(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        // Per the spec, redaction is an UpdateMessage with Body set to an empty string.
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var response = await _httpClient.PostAsync(MessageUri(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for only this application's own sending number's messages (From filter),
        // rather than filtering a wider answer after the fact.
        var messages = new List<TwilioMessage>();
        var query = $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json" +
            $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.ToString(DateSentFilterFormat, CultureInfo.InvariantCulture))}" +
            $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString(DateSentFilterFormat, CultureInfo.InvariantCulture))}" +
            "&PageSize=1000";

        string? nextUri = query;
        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(nextUri, cancellationToken);
            var page = await ReadAsync<ListMessageResponse>(response, cancellationToken);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    messages.Add(message.ToTwilioMessage());
                }
            }
            nextUri = page?.NextPageUri;
        }

        return messages;
    }

    private string MessagesUri() => $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageUri(string messageSid) => $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json";

    private static async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var message = await ReadAsync<MessageResource>(response, cancellationToken);
        return message!.ToTwilioMessage();
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken) where T : class
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response, content);
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private static TwilioApiException ToException(HttpResponseMessage response, string content)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ErrorResource>(content, JsonOptions);
            if (error != null)
            {
                return new TwilioApiException((int)response.StatusCode, error.Code,
                    error.Message ?? $"Twilio request failed with status {(int)response.StatusCode}.");
            }
        }
        catch (JsonException) { }
        return new TwilioApiException((int)response.StatusCode, null,
            $"Twilio request failed with status {(int)response.StatusCode}.");
    }

    // Shapes below mirror components/schemas/api.v2010.account.message and the
    // ListMessageResponse / error models from the OpenAPI specification.
    private sealed class MessageResource
    {
        [JsonPropertyName("sid")] public string? Sid { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("to")] public string? To { get; set; }
        [JsonPropertyName("from")] public string? From { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("error_code")] public int? ErrorCode { get; set; }
        [JsonPropertyName("error_message")] public string? ErrorMessage { get; set; }
        [JsonPropertyName("date_created")] public string? DateCreated { get; set; }
        [JsonPropertyName("date_sent")] public string? DateSent { get; set; }
        [JsonPropertyName("date_updated")] public string? DateUpdated { get; set; }

        public TwilioMessage ToTwilioMessage() => new()
        {
            Sid = Sid ?? string.Empty,
            Status = Status,
            To = To,
            From = From,
            Body = Body,
            ErrorCode = ErrorCode,
            ErrorMessage = ErrorMessage,
            DateCreated = ParseRfc2822(DateCreated),
            DateSent = ParseRfc2822(DateSent),
            DateUpdated = ParseRfc2822(DateUpdated)
        };

        // The spec types these timestamps as date-time-rfc-2822 (GMT).
        private static DateTimeOffset? ParseRfc2822(string? value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
    }

    private sealed class ListMessageResponse
    {
        [JsonPropertyName("messages")] public List<MessageResource>? Messages { get; set; }
        [JsonPropertyName("next_page_uri")] public string? NextPageUri { get; set; }
    }

    private sealed class ErrorResource
    {
        [JsonPropertyName("code")] public int? Code { get; set; }
        [JsonPropertyName("message")] public string? Message { get; set; }
        [JsonPropertyName("status")] public int? Status { get; set; }
        [JsonPropertyName("more_info")] public string? MoreInfo { get; set; }
    }
}
