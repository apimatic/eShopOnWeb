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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Programmable Messaging API (Messages resource).
/// Confirmed:
/// - Create: POST /2010-04-01/Accounts/{AccountSid}/Messages.json
///   https://www.twilio.com/docs/messaging/api/message-resource
/// - Schedule: ScheduleType=fixed, SendAt ISO-8601, MessagingServiceSid required
///   https://www.twilio.com/docs/messaging/features/message-scheduling
/// - Fetch: GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json
/// - Cancel scheduled: POST Status=canceled
/// - Redact body: POST Body=
///   https://www.twilio.com/docs/messaging/tutorials/how-to-retrieve-and-modify-message-history
/// - List by From + DateSent range (provider-side filter)
///   https://www.twilio.com/docs/messaging/api/message-resource#read-multiple-message-resources
/// Twilio:BaseUrl, when set, is used verbatim as the base for every messaging-API call.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;
    private readonly string _messagingBaseUrl;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> settings, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
        _messagingBaseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultBaseUrl
            : _settings.BaseUrl.Trim();
    }

    public Task<TwilioMessageSnapshot> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            form.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt.HasValue)
        {
            // Scheduling requires a Messaging Service; without it Twilio sends immediately.
            // https://www.twilio.com/docs/messaging/features/message-scheduling
            form.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            form.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync(HttpMethod.Post, MessagesCollectionPath(), form, cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(MessageInstancePath(messageSid)));
        request.Headers.Authorization = CreateBasicAuth();
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadSnapshotAsync(response, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), form, cancellationToken);
    }

    public Task<TwilioMessageSnapshot> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListMessagesFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // Provider-side From filter — do not list the whole account and filter locally.
        // DateSent inequality form is documented on the Messages list resource.
        var fromUtc = from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
                   $"&DateSent>={Uri.EscapeDataString(fromUtc)}" +
                   $"&DateSent<={Uri.EscapeDataString(toUtc)}" +
                   "&PageSize=1000";

        var results = new List<TwilioMessageSnapshot>();
        string? next = Combine(path);

        while (!string.IsNullOrEmpty(next))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = CreateBasicAuth();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio Messages list returned HTTP {StatusCode}.", (int)response.StatusCode);
                response.EnsureSuccessStatusCode();
            }

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions)
                       ?? new MessageListResponse();
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var snapshot = ToSnapshot(message);
                    if (snapshot is not null)
                    {
                        results.Add(snapshot);
                    }
                }
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : Combine(page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> SendFormAsync(
        HttpMethod method,
        string path,
        List<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Combine(path))
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = CreateBasicAuth();
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadSnapshotAsync(response, cancellationToken);
    }

    private async Task<TwilioMessageSnapshot> ReadSnapshotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio Messages call returned HTTP {StatusCode} with error code {ErrorCode}.",
                (int)response.StatusCode,
                TryReadErrorCode(payload));
            throw new HttpRequestException($"Twilio Messages call failed with HTTP {(int)response.StatusCode}.");
        }

        var parsed = JsonSerializer.Deserialize<MessageResource>(payload, JsonOptions);
        var snapshot = ToSnapshot(parsed);
        if (snapshot is null)
        {
            throw new InvalidOperationException("Twilio Messages response did not include a message SID.");
        }

        return snapshot;
    }

    private static TwilioMessageSnapshot? ToSnapshot(MessageResource? resource)
    {
        if (resource is null || string.IsNullOrEmpty(resource.Sid) || string.IsNullOrEmpty(resource.Status))
        {
            return null;
        }

        return new TwilioMessageSnapshot(
            resource.Sid,
            resource.Status,
            resource.ErrorCode,
            resource.ErrorMessage,
            resource.From,
            resource.Body,
            resource.DateCreated,
            resource.DateSent);
    }

    private static string? TryReadErrorCode(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("code", out var code))
            {
                return code.ToString();
            }
        }
        catch (JsonException)
        {
            // ignored — never log the raw payload; it may include destination numbers
        }

        return null;
    }

    private string MessagesCollectionPath() =>
        $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"/2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private string Combine(string uriOrPath)
    {
        string pathAndQuery;
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            pathAndQuery = absolute.PathAndQuery;
        }
        else
        {
            pathAndQuery = uriOrPath;
        }

        return $"{_messagingBaseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";
    }

    private AuthenticationHeaderValue CreateBasicAuth()
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    private sealed class MessageListResponse
    {
        public List<MessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class MessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
        public string? DateCreated { get; set; }
        public string? DateSent { get; set; }
    }
}
