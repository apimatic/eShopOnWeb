using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>
/// A deliberately small client for the operations defined by twilio_lookups_v2.yaml and
/// twilio_api_v2010.yaml. No provider SDK is used.
/// </summary>
public sealed class TwilioRestClient : ITwilioLookupClient, ITwilioMessagingClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly TwilioOptions _options;
    private readonly HttpClient _httpClient;

    public TwilioRestClient(IOptions<TwilioOptions> options, HttpMessageHandler? handler = null)
    {
        _options = options.Value;
        _httpClient = new HttpClient(handler ?? new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                ConnectTimeout = TimeSpan.FromSeconds(10)
            })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<TwilioLookupResponse> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        EnsureCredentials();
        var url = $"{TwilioOptions.LookupBaseUrl}/v2/PhoneNumbers/{Uri.EscapeDataString(phoneNumber)}";
        using var request = CreateRequest(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<TwilioLookupResponse>(response, cancellationToken);
    }

    public Task<TwilioMessage> SendAsync(
        string destination,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("MessagingServiceSid", _options.MessagingServiceSid),
            new("Body", content)
        };

        if (sendAt is not null)
        {
            values.Add(new("ScheduleType", "fixed"));
            values.Add(new("SendAt", sendAt.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
        }

        return SendFormAsync<TwilioMessage>(HttpMethod.Post, MessageCollectionUrl(), values, cancellationToken);
    }

    public async Task<TwilioMessage> FetchAsync(string messageSid, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, MessageUrl(messageSid));
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<TwilioMessage>(response, cancellationToken);
    }

    public Task<TwilioMessage> CancelAsync(string messageSid, CancellationToken cancellationToken) =>
        SendFormAsync<TwilioMessage>(HttpMethod.Post, MessageUrl(messageSid),
            new[] { new KeyValuePair<string, string>("Status", "canceled") }, cancellationToken);

    public Task<TwilioMessage> RedactAsync(string messageSid, CancellationToken cancellationToken) =>
        SendFormAsync<TwilioMessage>(HttpMethod.Post, MessageUrl(messageSid),
            new[] { new KeyValuePair<string, string>("Body", string.Empty) }, cancellationToken);

    public async Task<IReadOnlyList<TwilioMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["From"] = _options.FromNumber,
            ["DateSent>"] = from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["DateSent<"] = to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            ["PageSize"] = "1000"
        };
        var nextUrl = MessageCollectionUrl() + "?" + string.Join("&", query.Select(x =>
            $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}"));
        var messages = new List<TwilioMessage>();

        while (nextUrl is not null)
        {
            using var request = CreateRequest(HttpMethod.Get, nextUrl);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var page = await ReadAsync<TwilioMessagePage>(response, cancellationToken);
            messages.AddRange(page.Messages);
            nextUrl = string.IsNullOrWhiteSpace(page.NextPageUri)
                ? null
                : MessagingUrl(PathAndQuery(page.NextPageUri));
        }

        return messages
            .Where(x => x.DateSent is not null && x.DateSent >= from && x.DateSent <= to)
            .ToList();
    }

    private async Task<T> SendFormAsync<T>(
        HttpMethod method,
        string url,
        IEnumerable<KeyValuePair<string, string>> values,
        CancellationToken cancellationToken)
    {
        EnsureCredentials();
        using var request = CreateRequest(method, url);
        request.Content = new FormUrlEncodedContent(values);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        EnsureCredentials();
        var request = new HttpRequestMessage(method, url);
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            TwilioErrorResponse? error = null;
            try
            {
                error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                // The status and provider code are sufficient; response bodies may contain PII.
            }

            throw new TwilioApiException((int)response.StatusCode, error?.Code);
        }

        try
        {
            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
            return value ?? throw new TwilioApiException((int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TwilioApiException((int)response.StatusCode, null);
        }
    }

    private string MessageCollectionUrl() =>
        MessagingUrl($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");

    private string MessageUrl(string messageSid) =>
        MessagingUrl($"/2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");

    private string MessagingUrl(string pathAndQuery) =>
        $"{_options.MessagingBaseUrl.TrimEnd('/')}/{pathAndQuery.TrimStart('/')}";

    private static string PathAndQuery(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery;
        }

        return uri;
    }

    private void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(_options.AccountSid) ||
            string.IsNullOrWhiteSpace(_options.AuthToken) ||
            string.IsNullOrWhiteSpace(_options.FromNumber) ||
            string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio configuration is incomplete.");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
