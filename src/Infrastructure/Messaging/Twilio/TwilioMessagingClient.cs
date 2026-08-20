using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";
    private const int MaxListPages = 100;

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = ImmediateMessageFields(to, body);
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid must be configured to schedule messages.");
        }

        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(messageSid));
        request.Headers.Authorization = AuthHeader();
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber must be configured.");
        }

        var fromValue = Uri.EscapeDataString(_settings.FromNumber);
        var dateFrom = Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var dateTo = Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        var relative = $"{MessagesListPath()}?From={fromValue}&DateSent%3E={dateFrom}&DateSent%3C={dateTo}&PageSize=1000";

        var results = new List<ProviderMessage>();
        var next = relative;
        for (var page = 0; page < MaxListPages && !string.IsNullOrEmpty(next); page++)
        {
            var uri = ResolveMessagingUri(next);
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = AuthHeader();
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, TwilioHttp.JsonOptions)
                ?? new TwilioMessageListResponse();

            if (parsed.Messages is not null)
            {
                foreach (var message in parsed.Messages)
                {
                    var mapped = MapMessage(message);
                    if (mapped is not null)
                    {
                        results.Add(mapped);
                    }
                }
            }

            next = parsed.NextPageUri;
        }

        return results;
    }

    private Dictionary<string, string> ImmediateMessageFields(string to, string body)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body
        };

        if (!string.IsNullOrWhiteSpace(_settings.FromNumber))
        {
            fields["From"] = _settings.FromNumber;
        }

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        return fields;
    }

    private async Task<ProviderMessage> CreateMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesListPath())
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = AuthHeader();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(messageSid))
        {
            Content = new FormUrlEncodedContent(fields)
        };
        request.Headers.Authorization = AuthHeader();

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, cancellationToken);
        return await ReadMessageAsync(response, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var parsed = JsonSerializer.Deserialize<TwilioMessageResource>(payload, TwilioHttp.JsonOptions);
        var mapped = MapMessage(parsed);
        if (mapped is null)
        {
            _logger.LogWarning("Twilio message response did not include a SID.");
            throw new TwilioApiException((int)response.StatusCode, null);
        }

        return mapped;
    }

    private static ProviderMessage? MapMessage(TwilioMessageResource? resource)
    {
        if (resource is null || string.IsNullOrWhiteSpace(resource.Sid))
        {
            return null;
        }

        return new ProviderMessage(
            resource.Sid,
            resource.Status,
            resource.ErrorCode,
            resource.ErrorMessage,
            resource.From,
            resource.DateSent,
            resource.DateCreated,
            resource.Body);
    }

    private Uri ResolveMessagingUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        {
            return new Uri(_httpClient.BaseAddress ?? new Uri(DefaultMessagingBaseUrl + "/"), absolute.PathAndQuery);
        }

        return new Uri(_httpClient.BaseAddress ?? new Uri(DefaultMessagingBaseUrl + "/"), uri);
    }

    private string MessagesListPath() =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private AuthenticationHeaderValue AuthHeader() =>
        TwilioHttp.BasicAuth(_settings.AccountSid, _settings.AuthToken);

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.AccountSid) || string.IsNullOrWhiteSpace(_settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }
    }
}
