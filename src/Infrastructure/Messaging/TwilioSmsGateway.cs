using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioSmsGateway : ISmsGateway
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioSmsGateway> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _options.FromNumber),
            new("Body", request.Body)
        };

        if (request.SendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _options.MessagingServiceSid));
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }

        return PostMessageAsync(MessagesCollectionPath(), fields, "Send message", cancellationToken);
    }

    public async Task<SmsMessage> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageResourcePath(providerSid));
        var response = await SendWithoutLoggingUriAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioJson.ThrowIfFailed(response, body, "Fetch message");
        return Map(TwilioJson.Deserialize<TwilioMessageResponse>(body));
    }

    public Task<SmsMessage> CancelAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Status", "canceled")
        };
        return PostMessageAsync(MessageResourcePath(providerSid), fields, "Cancel message", cancellationToken);
    }

    public async Task<SmsMessage> RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        // An empty Body must be sent as the form field `Body=` — that is the provider's
        // documented redaction signal. StringContent keeps the empty value visible.
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageResourcePath(providerSid))
        {
            Content = new StringContent("Body=", Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        var response = await SendWithoutLoggingUriAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioJson.ThrowIfFailed(response, body, "Redact message");
        return Map(TwilioJson.Deserialize<TwilioMessageResponse>(body));
    }

    public async Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessage>();
        var firstPath = MessagesCollectionPath()
            + "?From=" + Uri.EscapeDataString(_options.FromNumber)
            + "&DateSent%3E=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
            + "&DateSent%3C=" + Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))
            + "&PageSize=1000";

        string? next = firstPath;
        var pages = 0;
        while (!string.IsNullOrWhiteSpace(next) && pages < 100)
        {
            pages++;
            using var request = new HttpRequestMessage(HttpMethod.Get, ResolveMessagingUri(next));
            var response = await SendWithoutLoggingUriAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            TwilioJson.ThrowIfFailed(response, body, "List messages");
            var page = TwilioJson.Deserialize<TwilioMessageListResponse>(body);
            if (page.Messages != null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(Map(message));
                }
            }

            next = page.NextPageUri;
        }

        return results;
    }

    private async Task<SmsMessage> PostMessageAsync(
        string relativePath,
        IEnumerable<KeyValuePair<string, string>> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = new FormUrlEncodedContent(fields)
        };

        var response = await SendWithoutLoggingUriAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioJson.ThrowIfFailed(response, body, operation);
        return Map(TwilioJson.Deserialize<TwilioMessageResponse>(body));
    }

    private async Task<HttpResponseMessage> SendWithoutLoggingUriAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogWarning("Messaging API call failed before a response was received");
            throw;
        }
    }

    private string MessagesCollectionPath()
    {
        return $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    }

    private string MessageResourcePath(string sid)
    {
        return $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";
    }

    private Uri ResolveMessagingUri(string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(new Uri(_options.ResolveMessagingBaseUrl()), absolute.PathAndQuery);
        }

        return new Uri(_httpClient.BaseAddress ?? new Uri(_options.ResolveMessagingBaseUrl()), nextPageUri);
    }

    private static SmsMessage Map(TwilioMessageResponse payload)
    {
        return new SmsMessage
        {
            Sid = payload.Sid ?? string.Empty,
            Status = payload.Status,
            Body = payload.Body,
            To = payload.To,
            From = payload.From,
            DateSent = TwilioDate.Parse(payload.DateSent),
            DateCreated = TwilioDate.Parse(payload.DateCreated),
            ErrorCode = payload.ErrorCode
        };
    }
}
