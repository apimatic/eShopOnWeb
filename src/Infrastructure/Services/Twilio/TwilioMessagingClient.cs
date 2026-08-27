using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against the
/// twilio_api_v2010 OpenAPI document (the authoritative contract):
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages.json        CreateMessage
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages.json        ListMessage
///   GET    /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  FetchMessage
///   POST   /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  UpdateMessage
/// Auth: HTTP Basic with AccountSid:AuthToken (security scheme accountSid_authToken).
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultBaseUrl = "https://api.twilio.com";
    private const string DateSentFormat = "yyyy-MM-ddTHH:mm:ssZ";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly string _baseUrl;
    private readonly string _messagesPath;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _settings = options.Value;
        _baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl!.TrimEnd('/');
        _messagesPath = $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}")));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<TwilioMessage> CreateMessageAsync(string to, string body, DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("Body", body),
            new("From", _settings.FromNumber)
        };

        if (sendAt.HasValue)
        {
            // Scheduling requires a Messaging Service (ScheduleType/SendAt per CreateMessageRequest).
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid must be configured to schedule messages.");
            }
            form.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        using var response = await _httpClient.PostAsync(_baseUrl + _messagesPath, new FormUrlEncodedContent(form), cancellationToken);
        return await ReadJsonAsync<TwilioMessage>(response, cancellationToken);
    }

    public async Task<TwilioMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        return await ReadJsonAsync<TwilioMessage>(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only
        // (From filter), rather than filtering a wider answer after the fact.
        var query = $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
                    $"&DateSent%3E={Uri.EscapeDataString(from.UtcDateTime.AddSeconds(-1).ToString(DateSentFormat))}" +
                    $"&DateSent%3C={Uri.EscapeDataString(to.UtcDateTime.ToString(DateSentFormat))}" +
                    "&PageSize=1000";

        var messages = new List<TwilioMessage>();
        string? nextUri = _messagesPath + query;
        while (nextUri != null)
        {
            using var response = await _httpClient.GetAsync(_baseUrl + nextUri, cancellationToken);
            var page = await ReadJsonAsync<TwilioListMessagesResponse>(response, cancellationToken);
            if (page.Messages != null)
            {
                messages.AddRange(page.Messages);
            }
            nextUri = page.NextPageUri;
            cancellationToken.ThrowIfCancellationRequested();
        }
        return messages;
    }

    public async Task<TwilioMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return await UpdateMessageAsync(messageSid, new List<KeyValuePair<string, string>> { new("Status", "canceled") }, cancellationToken);
    }

    public async Task<TwilioMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return await UpdateMessageAsync(messageSid, new List<KeyValuePair<string, string>> { new("Body", "") }, cancellationToken);
    }

    private async Task<TwilioMessage> UpdateMessageAsync(string messageSid, List<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(MessageUrl(messageSid), new FormUrlEncodedContent(form), cancellationToken);
        return await ReadJsonAsync<TwilioMessage>(response, cancellationToken);
    }

    private string MessageUrl(string messageSid) => $"{_baseUrl}/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private static async Task<T> ReadJsonAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, content);
        }
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
            ?? throw new TwilioApiException(response.StatusCode, null, "Empty response body from the provider.");
    }

    private static TwilioApiException ToException(HttpStatusCode statusCode, string content)
    {
        int? code = null;
        string? message = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number)
            {
                code = codeElement.GetInt32();
            }
            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                message = messageElement.GetString();
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; report the status code only.
        }
        return new TwilioApiException(statusCode, code, message);
    }
}
