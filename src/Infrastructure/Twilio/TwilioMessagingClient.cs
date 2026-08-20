using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingService
{
    private readonly HttpClient _http;
    private readonly TwilioSettings _settings;
    private readonly Uri _baseAddress;

    public TwilioMessagingClient(HttpClient http, IOptions<TwilioSettings> options)
    {
        _http = http;
        _settings = options.Value;
        _baseAddress = TwilioHttp.MessagingBaseAddress(_settings);
        _http.BaseAddress = _baseAddress;
        TwilioHttp.ConfigureBasicAuth(_http, _settings);
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioMessage> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to schedule a message.");
            }

            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }
        else if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var response = await _http.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var response = await _http.GetAsync(MessageInstancePath(messageSid), cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessage>> ListFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var query = new Dictionary<string, string?>
        {
            ["From"] = fromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["DateSent<"] = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["PageSize"] = "1000"
        };

        var requestUri = AppendQuery(MessagesCollectionPath(), query);
        var results = new List<TwilioMessage>();
        var pages = 0;

        while (!string.IsNullOrEmpty(requestUri) && pages < 50)
        {
            pages++;
            using var response = await _http.GetAsync(ResolveMessagingUri(requestUri), cancellationToken);
            await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var page = await JsonSerializer.DeserializeAsync<MessageListResponse>(stream, TwilioHttp.JsonOptions, cancellationToken)
                ?? new MessageListResponse();

            if (page.Messages is not null)
            {
                results.AddRange(page.Messages.Select(Map));
            }

            requestUri = string.IsNullOrWhiteSpace(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    public Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateAsync(messageSid, new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<TwilioMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateAsync(messageSid, new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    private async Task<TwilioMessage> UpdateAsync(
        string messageSid,
        IEnumerable<KeyValuePair<string, string>> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var content = new FormUrlEncodedContent(fields);
        using var response = await _http.PostAsync(MessageInstancePath(messageSid), content, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private static string AppendQuery(string path, Dictionary<string, string?> query)
    {
        var builder = new StringBuilder(path);
        builder.Append('?');
        var first = true;
        foreach (var pair in query)
        {
            if (string.IsNullOrEmpty(pair.Value))
            {
                continue;
            }

            if (!first)
            {
                builder.Append('&');
            }

            first = false;
            builder.Append(Uri.EscapeDataString(pair.Key));
            builder.Append('=');
            builder.Append(Uri.EscapeDataString(pair.Value));
        }

        return builder.ToString();
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri(_baseAddress, absolute.PathAndQuery);
        }

        return new Uri(_baseAddress, uriOrPath.TrimStart('/'));
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio credentials are not configured.");
        }

        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is not configured.");
        }
    }

    private static async Task<TwilioMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var resource = await JsonSerializer.DeserializeAsync<MessageResource>(stream, TwilioHttp.JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Twilio returned an empty message resource.");
        return Map(resource);
    }

    private static TwilioMessage Map(MessageResource resource)
        => new(
            resource.Sid ?? string.Empty,
            resource.Status,
            resource.From,
            resource.To,
            resource.Body,
            resource.ErrorCode,
            resource.DateSent,
            resource.DateCreated);

    private sealed class MessageResource
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? From { get; set; }
        public string? To { get; set; }
        public string? Body { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class MessageListResponse
    {
        public List<MessageResource>? Messages { get; set; }
        public string? NextPageUri { get; set; }
    }
}
