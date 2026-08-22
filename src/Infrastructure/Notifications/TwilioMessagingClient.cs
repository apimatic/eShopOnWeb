using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public class TwilioMessagingClient : TwilioHttpClientBase, ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        HttpClient httpClient,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
        : base(options, logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress ??= MessagingBaseUri;
    }

    public async Task<SmsMessageResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", to),
            new("From", Settings.FromNumber),
            new("Body", body)
        };

        if (!string.IsNullOrWhiteSpace(Settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", Settings.MessagingServiceSid));
        }

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(Settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
            }

            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
        }

        using var response = await SendWithRetryAsync(
            _httpClient,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath());
                request.Content = new FormUrlEncodedContent(fields);
                return request;
            },
            retryServerErrors: false,
            cancellationToken);

        await EnsureSuccessAsync(response, "SendMessage");
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<SmsMessageResult> FetchAsync(string sid, CancellationToken cancellationToken = default)
    {
        using var response = await SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(sid)),
            retryServerErrors: true,
            cancellationToken);

        await EnsureSuccessAsync(response, "FetchMessage");
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<SmsMessageResult> CancelAsync(string sid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };

        using var response = await SendWithRetryAsync(
            _httpClient,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(sid));
                request.Content = new FormUrlEncodedContent(fields);
                return request;
            },
            retryServerErrors: true,
            cancellationToken);

        await EnsureSuccessAsync(response, "CancelMessage");
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<SmsMessageResult> RedactBodyAsync(string sid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Body", string.Empty)
        };

        using var response = await SendWithRetryAsync(
            _httpClient,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(sid));
                request.Content = new FormUrlEncodedContent(fields);
                return request;
            },
            retryServerErrors: true,
            cancellationToken);

        await EnsureSuccessAsync(response, "RedactMessage");
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromIso = from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var firstPage = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(Settings.FromNumber)}&PageSize=1000&DateSent%3E={Uri.EscapeDataString(fromIso)}&DateSent%3C={Uri.EscapeDataString(toIso)}";

        var results = new List<SmsMessageResult>();
        string? next = firstPage;

        while (!string.IsNullOrWhiteSpace(next))
        {
            var pageUri = ResolveMessagingUri(next);
            using var response = await SendWithRetryAsync(
                _httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, pageUri),
                retryServerErrors: true,
                cancellationToken);

            await EnsureSuccessAsync(response, "ListMessages");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessageListResponse>(stream, JsonOptions.Serializer, cancellationToken)
                       ?? new MessageListResponse();

            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToResult(message));
                }
            }

            next = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : page.NextPageUri;
            if (next != null)
            {
                _logger.LogInformation("Fetching next messaging list page.");
            }
        }

        return results;
    }

    private Uri MessagingBaseUri
    {
        get
        {
            var configured = string.IsNullOrWhiteSpace(Settings.BaseUrl)
                ? "https://api.twilio.com"
                : Settings.BaseUrl.Trim();
            if (!configured.EndsWith('/'))
            {
                configured += "/";
            }

            return new Uri(configured, UriKind.Absolute);
        }
    }

    private string MessagesCollectionPath() => $"2010-04-01/Accounts/{Settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid) => $"2010-04-01/Accounts/{Settings.AccountSid}/Messages/{sid}.json";

    private Uri ResolveMessagingUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        {
            return new Uri(MessagingBaseUri, absolute.PathAndQuery);
        }

        return new Uri(MessagingBaseUri, uri);
    }

    private static async Task<SmsMessageResult> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<TwilioMessageResource>(stream, JsonOptions.Serializer, cancellationToken)
                      ?? new TwilioMessageResource();
        return ToResult(payload);
    }

    private static SmsMessageResult ToResult(TwilioMessageResource resource)
    {
        return new SmsMessageResult(
            resource.Sid ?? string.Empty,
            resource.Status ?? string.Empty,
            resource.Body,
            resource.ErrorCode,
            ParseTwilioDate(resource.DateSent),
            ParseTwilioDate(resource.DateCreated));
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class MessageListResponse
    {
        public List<TwilioMessageResource>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioMessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }
}
