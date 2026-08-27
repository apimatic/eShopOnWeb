using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingHost = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string FromNumber => _options.FromNumber;

    public async Task<ProviderMessage> SendAsync(SendProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("To", request.To),
            new("From", _options.FromNumber),
            new("Body", request.Body)
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            form.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        }

        if (request.SendAt is { } sendAt)
        {
            form.Add(new("ScheduleType", "fixed"));
            form.Add(new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }

        var uri = MessagesCollectionUri();
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => CreateFormRequest(HttpMethod.Post, uri, form),
            retryServerErrors: false,
            cancellationToken);

        await TwilioHttp.EnsureSuccessAsync(response);
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return TwilioMessageMapper.ToProviderMessage(dto);
    }

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var uri = MessageInstanceUri(messageSid);
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => CreateJsonGet(uri),
            retryServerErrors: true,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await TwilioHttp.EnsureSuccessAsync(response);
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return TwilioMessageMapper.ToProviderMessage(dto);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListByFromNumberAsync(
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken = default)
    {
        var startDate = DateOnly.FromDateTime(rangeStart.UtcDateTime).AddDays(-1).ToString("yyyy-MM-dd");
        var endDate = DateOnly.FromDateTime(rangeEnd.UtcDateTime).AddDays(1).ToString("yyyy-MM-dd");

        var firstUri = $"{MessagesCollectionUri()}?From={Uri.EscapeDataString(_options.FromNumber)}" +
                       $"&DateSent%3E={Uri.EscapeDataString(startDate)}" +
                       $"&DateSent%3C={Uri.EscapeDataString(endDate)}" +
                       "&PageSize=1000";

        var results = new List<ProviderMessage>();
        string? next = firstUri;

        while (!string.IsNullOrEmpty(next))
        {
            var pageUri = next;
            using var response = await TwilioHttp.SendWithRetryAsync(
                _httpClient,
                () => CreateJsonGet(pageUri),
                retryServerErrors: true,
                cancellationToken);

            await TwilioHttp.EnsureSuccessAsync(response);
            var page = await TwilioHttp.ReadJsonAsync<TwilioMessageListDto>(response);
            foreach (var message in page.Messages)
            {
                results.Add(TwilioMessageMapper.ToProviderMessage(message));
            }

            next = string.IsNullOrEmpty(page.NextPageUri)
                ? null
                : ResolveMessagingUri(page.NextPageUri);
        }

        return results;
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new List<KeyValuePair<string, string>> { new("Body", string.Empty) }, cancellationToken);

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new List<KeyValuePair<string, string>> { new("Status", "canceled") }, cancellationToken);

    private async Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        List<KeyValuePair<string, string>> form,
        CancellationToken cancellationToken)
    {
        var uri = MessageInstanceUri(messageSid);
        using var response = await TwilioHttp.SendWithRetryAsync(
            _httpClient,
            () => CreateFormRequest(HttpMethod.Post, uri, form),
            retryServerErrors: true,
            cancellationToken);

        await TwilioHttp.EnsureSuccessAsync(response);
        var dto = await TwilioHttp.ReadJsonAsync<TwilioMessageDto>(response);
        return TwilioMessageMapper.ToProviderMessage(dto);
    }

    private HttpRequestMessage CreateFormRequest(HttpMethod method, string uri, List<KeyValuePair<string, string>> form)
    {
        var request = new HttpRequestMessage(method, uri)
        {
            Content = new FormUrlEncodedContent(form)
        };
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private HttpRequestMessage CreateJsonGet(string uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string MessagesCollectionUri()
        => CombineMessagingBase($"2010-04-01/Accounts/{_options.AccountSid}/Messages.json");

    private string MessageInstanceUri(string sid)
        => CombineMessagingBase($"2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json");

    private string CombineMessagingBase(string relativePath)
    {
        var root = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingHost
            : _options.BaseUrl.TrimEnd('/');
        return $"{root}/{relativePath.TrimStart('/')}";
    }

    private string ResolveMessagingUri(string nextPageUri)
    {
        var root = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingHost
            : _options.BaseUrl.TrimEnd('/');

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return root + absolute.PathAndQuery;
        }

        if (!nextPageUri.StartsWith('/'))
        {
            nextPageUri = "/" + nextPageUri;
        }

        return root + nextPageUri;
    }
}
