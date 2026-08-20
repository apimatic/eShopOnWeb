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
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessageClient : ITwilioMessageClient
{
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessageClient> _logger;

    public TwilioMessageClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        IAppLogger<TwilioMessageClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<TwilioMessageSnapshot> CreateAsync(TwilioCreateMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _settings.FromNumber),
            new("Body", request.Body)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new ProviderException("Scheduled messages require Twilio:MessagingServiceSid.");
            }

            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>(
                "SendAt",
                request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(httpRequest);

        return await SendForSnapshotAsync(httpRequest, "create", cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageResourcePath(messageSid));
        ApplyBasicAuth(httpRequest);
        return await SendForSnapshotAsync(httpRequest, "fetch", cancellationToken);
    }

    public async Task<TwilioMessageSnapshot> UpdateAsync(
        string messageSid,
        string? body,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>();
        if (body != null)
        {
            fields.Add(new KeyValuePair<string, string>("Body", body));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            fields.Add(new KeyValuePair<string, string>("Status", status));
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageResourcePath(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        ApplyBasicAuth(httpRequest);
        return await SendForSnapshotAsync(httpRequest, "update", cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageSnapshot>();
        var fromUtc = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toUtc = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        // Official list filter: From plus inclusive DateSent inequalities, paging via next_page_uri.
        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(fromNumber)}" +
                   $"&DateSent%3E={Uri.EscapeDataString(fromUtc)}" +
                   $"&DateSent%3C={Uri.EscapeDataString(toUtc)}" +
                   "&PageSize=1000";

        string? next = path;
        while (!string.IsNullOrWhiteSpace(next))
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, ToRelativeMessagingPath(next));
            ApplyBasicAuth(httpRequest);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Twilio list messages failed with HTTP {StatusCode}.", (int)response.StatusCode);
                throw new ProviderException("The messaging provider could not list messages for reconciliation.");
            }

            var page = JsonSerializer.Deserialize<MessageListResponse>(payload, JsonOptions);
            if (page?.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            next = page?.NextPageUri;
        }

        return results;
    }

    private async Task<TwilioMessageSnapshot> SendForSnapshotAsync(
        HttpRequestMessage httpRequest,
        string operation,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode}.", operation, (int)response.StatusCode);
            throw new ProviderException($"The messaging provider could not {operation} the message.");
        }

        var parsed = JsonSerializer.Deserialize<MessageResource>(payload, JsonOptions);
        if (parsed == null || string.IsNullOrWhiteSpace(parsed.Sid))
        {
            throw new ProviderException("The messaging provider returned an unreadable message resource.");
        }

        return ToSnapshot(parsed);
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";

    private string MessageResourcePath(string messageSid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static string ToRelativeMessagingPath(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return uriOrPath.TrimStart('/');
    }

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var raw = $"{_settings.AccountSid}:{_settings.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
    }

    private static TwilioMessageSnapshot ToSnapshot(MessageResource resource)
    {
        return new TwilioMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status ?? "unknown",
            resource.Body,
            resource.To,
            resource.From,
            resource.ErrorCode,
            resource.ErrorMessage,
            resource.DateSent);
    }

    private sealed class MessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
