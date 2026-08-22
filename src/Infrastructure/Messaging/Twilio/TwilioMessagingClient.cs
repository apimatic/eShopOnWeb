using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<TwilioMessageResult> CreateMessageAsync(CreateTwilioMessageRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("Body", request.Body),
            new("From", _settings.FromNumber)
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields.Add(new KeyValuePair<string, string>("MessagingServiceSid", _settings.MessagingServiceSid));
        }

        if (request.SendAt.HasValue)
        {
            fields.Add(new KeyValuePair<string, string>("ScheduleType", "fixed"));
            fields.Add(new KeyValuePair<string, string>("SendAt", request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)));
        }

        using var content = new FormUrlEncodedContent(fields);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionPath())
        {
            Content = content
        };

        var resource = await SendForResourceAsync(httpRequest, cancellationToken);
        return Map(resource);
    }

    public async Task<TwilioMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(messageSid));
        var resource = await SendForResourceAsync(httpRequest, cancellationToken);
        return Map(resource);
    }

    public async Task<TwilioMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var fields = new List<KeyValuePair<string, string>> { new("Status", "canceled") };
        using var content = new FormUrlEncodedContent(fields);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(messageSid))
        {
            Content = content
        };
        var resource = await SendForResourceAsync(httpRequest, cancellationToken);
        return Map(resource);
    }

    public async Task<TwilioMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var fields = new List<KeyValuePair<string, string>> { new("Body", string.Empty) };
        using var content = new FormUrlEncodedContent(fields);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageInstancePath(messageSid))
        {
            Content = content
        };
        var resource = await SendForResourceAsync(httpRequest, cancellationToken);
        return Map(resource);
    }

    public async Task<IReadOnlyList<TwilioMessageResult>> ListMessagesFromAsync(TwilioMessageListRequest request, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var results = new List<TwilioMessageResult>();
        var after = request.DateSentAfter.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var before = request.DateSentBefore.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var query = new List<string>
        {
            "From=" + Uri.EscapeDataString(request.From),
            "DateSent%3E=" + Uri.EscapeDataString(after),
            "DateSent%3C=" + Uri.EscapeDataString(before),
            "PageSize=1000"
        };

        string? next = MessagesCollectionPath() + "?" + string.Join("&", query);
        var client = CreateMessagingClient();

        while (!string.IsNullOrWhiteSpace(next))
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, next);
            httpRequest.Headers.Authorization = TwilioAuth.CreateHeader(_settings.AccountSid, _settings.AuthToken);

            using var response = await client.SendAsync(httpRequest, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResponse>(TwilioJson.Options, cancellationToken)
                ?? new TwilioMessageListResponse();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(Map(message));
                }
            }

            next = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : TwilioUri.CombineRelative(client.BaseAddress!, page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageResource> SendForResourceAsync(HttpRequestMessage httpRequest, CancellationToken cancellationToken)
    {
        var client = CreateMessagingClient();
        httpRequest.Headers.Authorization = TwilioAuth.CreateHeader(_settings.AccountSid, _settings.AuthToken);

        using var response = await client.SendAsync(httpRequest, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(TwilioJson.Options, cancellationToken);
        if (resource is null)
        {
            throw new TwilioClientException((int)response.StatusCode, null);
        }

        return resource;
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var providerCode = await TryReadProviderCodeAsync(response, cancellationToken);
        _logger.LogWarning("Messaging API returned HTTP {StatusCode} (provider code {ProviderCode})", (int)response.StatusCode, providerCode);
        throw new TwilioClientException((int)response.StatusCode, providerCode);
    }

    private static async Task<int?> TryReadProviderCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioApiError>(TwilioJson.Options, cancellationToken);
            return error?.Code;
        }
        catch
        {
            return null;
        }
    }

    private HttpClient CreateMessagingClient()
        => _httpClientFactory.CreateClient(TwilioServiceCollectionExtensions.MessagingClientName);

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{sid}.json";

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

    private static TwilioMessageResult Map(TwilioMessageResource resource)
    {
        return new TwilioMessageResult
        {
            Sid = resource.Sid,
            Status = resource.Status,
            ErrorCode = resource.ErrorCode,
            Body = resource.Body,
            DateSent = resource.DateSent,
            DateCreated = resource.DateCreated,
            Direction = resource.Direction,
            From = resource.From
        };
    }
}
