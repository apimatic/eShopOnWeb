using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Extensions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;

    public TwilioMessagingClient(IHttpClientFactory httpClientFactory, IOptions<TwilioSettings> options)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
    }

    public async Task<ProviderMessage> CreateMessageAsync(CreateProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", request.To)
        };

        if (!string.IsNullOrWhiteSpace(request.From))
        {
            values.Add(new("From", request.From));
        }

        if (!string.IsNullOrWhiteSpace(request.Body))
        {
            values.Add(new("Body", request.Body));
        }

        if (!string.IsNullOrWhiteSpace(request.MessagingServiceSid))
        {
            values.Add(new("MessagingServiceSid", request.MessagingServiceSid));
        }

        if (!string.IsNullOrWhiteSpace(request.ScheduleType))
        {
            values.Add(new("ScheduleType", request.ScheduleType));
        }

        if (request.SendAt.HasValue)
        {
            values.Add(new("SendAt", request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")));
        }

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json";
        using var content = CreateFormContent(values);
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken);
        var resource = await ReadMessageAsync(response, cancellationToken);
        return Map(resource);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
        var resource = await ReadMessageAsync(response, cancellationToken);
        return Map(resource);
    }

    public async Task<ProviderMessage> UpdateMessageAsync(string messageSid, UpdateProviderMessageRequest request, CancellationToken cancellationToken = default)
    {
        var values = new List<KeyValuePair<string, string>>();
        if (request.Body != null)
        {
            values.Add(new("Body", request.Body));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            values.Add(new("Status", request.Status));
        }

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";
        using var content = CreateFormContent(values);
        using var response = await SendAsync(HttpMethod.Post, path, content, cancellationToken);
        if ((int)response.StatusCode == 409)
        {
            return await FetchMessageAsync(messageSid, cancellationToken);
        }

        var resource = await ReadMessageAsync(response, cancellationToken);
        return Map(resource);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(ListProviderMessagesRequest request, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.From))
        {
            query.Add(Uri.EscapeDataString("From") + "=" + Uri.EscapeDataString(request.From));
        }

        if (request.DateSentAfter.HasValue)
        {
            query.Add(Uri.EscapeDataString("DateSent>") + "=" + Uri.EscapeDataString(ToTwilioDate(request.DateSentAfter.Value)));
        }

        if (request.DateSentBefore.HasValue)
        {
            query.Add(Uri.EscapeDataString("DateSent<") + "=" + Uri.EscapeDataString(ToTwilioDate(request.DateSentBefore.Value)));
        }

        query.Add("PageSize=1000");

        var path = $"/2010-04-01/Accounts/{Uri.EscapeDataString(_settings.AccountSid)}/Messages.json?{string.Join("&", query)}";
        var pages = 0;
        while (!string.IsNullOrEmpty(path) && pages < 100)
        {
            using var response = await SendAsync(HttpMethod.Get, path, content: null, cancellationToken);
            var payload = await ReadAsync<TwilioListMessageResponse>(response, cancellationToken);
            if (payload.Messages != null)
            {
                foreach (var message in payload.Messages)
                {
                    results.Add(Map(message));
                }
            }

            path = payload.NextPageUri;
            pages++;
        }

        return results;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathOrUri, HttpContent? content, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(method, Combine(pathOrUri));
        ApplyAuth(request);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = content;
        return await client.SendAsync(request, cancellationToken);
    }

    private void ApplyAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_settings.AccountSid}:{_settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private Uri Combine(string pathOrUri)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? DefaultBaseUrl : _settings.BaseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
        {
            baseUrl += "/";
        }

        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        if (Uri.TryCreate(pathOrUri, UriKind.Absolute, out var absolute))
        {
            var relative = absolute.PathAndQuery.TrimStart('/');
            return new Uri(baseUri, relative);
        }

        if (pathOrUri.Contains('?', StringComparison.Ordinal))
        {
            var split = pathOrUri.Split('?', 2);
            var combined = new Uri(baseUri, split[0].TrimStart('/'));
            return new Uri(combined.GetLeftPart(UriPartial.Path) + "?" + split[1], UriKind.Absolute);
        }

        return new Uri(baseUri, pathOrUri.TrimStart('/'));
    }

    private static HttpContent CreateFormContent(IReadOnlyList<KeyValuePair<string, string>> values)
    {
        var encoded = string.Join("&", values.Select(v => Uri.EscapeDataString(v.Key) + "=" + Uri.EscapeDataString(v.Value)));
        return new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
    }

    private static async Task<TwilioMessageResource> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        return await ReadAsync<TwilioMessageResource>(response, cancellationToken);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioApiException(
                (int)response.StatusCode,
                $"Twilio messaging request failed with status {(int)response.StatusCode}: {PhoneNumberRedactor.Redact(body)}");
        }

        var parsed = JsonSerializer.Deserialize<T>(body, TwilioJson.Options);
        if (parsed == null)
        {
            throw new TwilioApiException((int)response.StatusCode, "Twilio messaging request returned an empty body.");
        }

        return parsed;
    }

    private static ProviderMessage Map(TwilioMessageResource resource)
    {
        return new ProviderMessage
        {
            Sid = resource.Sid,
            Status = resource.Status,
            Body = resource.Body,
            From = resource.From,
            To = resource.To,
            ErrorCode = resource.ErrorCode,
            ErrorMessage = resource.ErrorMessage,
            DateCreated = TwilioJson.ParseRfc2822(resource.DateCreated),
            DateSent = TwilioJson.ParseRfc2822(resource.DateSent),
            DateUpdated = TwilioJson.ParseRfc2822(resource.DateUpdated)
        };
    }

    private static string ToTwilioDate(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }
}
