using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioSettings _settings;
    private readonly Uri _baseAddress;
    private readonly AuthenticationHeaderValue _authorization;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioSettings> options)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _baseAddress = ResolveBaseAddress(_settings.BaseUrl);
        _authorization = TwilioHttp.CreateBasicAuth(_settings.AccountSid, _settings.AuthToken);
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public string FromNumber => _settings.FromNumber;

    public Task<TwilioMessageResource> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body
        };
        return CreateMessageAsync(fields, allowRetryOnServerError: false, cancellationToken);
    }

    public Task<TwilioMessageResource> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _settings.FromNumber,
            ["Body"] = body,
            ["MessagingServiceSid"] = _settings.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
        };
        return CreateMessageAsync(fields, allowRetryOnServerError: false, cancellationToken);
    }

    public async Task<TwilioMessageResource> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var url = MessageUri(messageSid);
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            _authorization,
            allowRetryOnServerError: true,
            cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, "Fetch message");
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return ToResource(dto);
    }

    public Task<TwilioMessageResource> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, "Cancel message", cancellationToken);
    }

    public Task<TwilioMessageResource> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, "Redact message body", cancellationToken);
    }

    public async Task<IReadOnlyList<TwilioMessageResource>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<TwilioMessageResource>();
        var firstPath =
            $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json" +
            $"?From={Uri.EscapeDataString(_settings.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(from.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}" +
            $"&DateSent%3C={Uri.EscapeDataString(to.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"))}" +
            "&PageSize=1000";

        Uri? next = Combine(_baseAddress, firstPath);
        while (next is not null)
        {
            var pageUrl = next;
            using var response = await TwilioHttp.SendWithRetryAsync(
                _httpClient,
                () => new HttpRequestMessage(HttpMethod.Get, pageUrl),
                _authorization,
                allowRetryOnServerError: true,
                cancellationToken);
            await TwilioHttp.EnsureSuccessAsync(response, "List messages");
            var page = await TwilioHttp.ReadJsonAsync<TwilioMessageListDto>(response);
            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToResource(message));
                }
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : Combine(_baseAddress, page.NextPageUri);
        }

        return results;
    }

    private async Task<TwilioMessageResource> CreateMessageAsync(
        Dictionary<string, string> fields,
        bool allowRetryOnServerError,
        CancellationToken cancellationToken)
    {
        var url = new Uri(_baseAddress, $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json");
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) },
            _authorization,
            allowRetryOnServerError,
            cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, "Create message");
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return ToResource(dto);
    }

    private async Task<TwilioMessageResource> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        var url = MessageUri(messageSid);
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) },
            _authorization,
            allowRetryOnServerError: false,
            cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, operation);
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return ToResource(dto);
    }

    private Uri MessageUri(string messageSid)
    {
        return new Uri(
            _baseAddress,
            $"2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");
    }

    private static TwilioMessageResource ToResource(TwilioMessageDto dto)
    {
        return new TwilioMessageResource(
            dto.Sid,
            dto.Status,
            dto.ErrorCode,
            dto.Body,
            dto.From,
            dto.To,
            dto.DateSent,
            dto.DateCreated);
    }

    private static Uri ResolveBaseAddress(string? configured)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? DefaultMessagingBaseUrl : configured.Trim();
        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private static Uri Combine(Uri baseAddress, string pathAndQuery)
    {
        if (Uri.TryCreate(pathAndQuery, UriKind.Absolute, out var absolute))
        {
            return new Uri(baseAddress, absolute.PathAndQuery);
        }

        return new Uri(baseAddress, pathAndQuery.TrimStart('/'));
    }
}
