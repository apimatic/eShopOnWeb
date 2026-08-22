using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ISmsMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(IHttpClientFactory httpClientFactory, IOptions<TwilioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string FromNumber => _options.FromNumber;

    public Task<SmsMessageResult> SendAsync(SendSmsRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            form["From"] = _options.FromNumber;
        }

        if (request.SendAt is not null)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
            }

            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid) && string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            form["MessagingServiceSid"] = _options.MessagingServiceSid;
        }

        return SendFormAsync(HttpMethod.Post, MessagesCollectionPath(), form, cancellationToken);
    }

    public async Task<SmsMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, MessageInstancePath(messageSid));
        ApplyBasicAuth(request);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToException(response, payload);
        }

        return TwilioResponseParser.ToMessageResult(JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions));
    }

    public Task<SmsMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), form, cancellationToken);
    }

    public Task<SmsMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return SendFormAsync(HttpMethod.Post, MessageInstancePath(messageSid), form, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is required to reconcile messages.");
        }

        var results = new List<SmsMessageResult>();
        var path = $"{MessagesCollectionPath()}?From={Uri.EscapeDataString(_options.FromNumber)}" +
                   $"&DateSent%3E={Uri.EscapeDataString(FormatTimestamp(from))}" +
                   $"&DateSent%3C={Uri.EscapeDataString(FormatTimestamp(to))}" +
                   "&PageSize=1000";

        var client = CreateClient();

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TrimLeadingSlash(path));
            ApplyBasicAuth(request);
            using var response = await client.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioResponseParser.ToException(response, payload);
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions)
                ?? new TwilioMessageListResponse();

            foreach (var message in page.Messages)
            {
                results.Add(TwilioResponseParser.ToMessageResult(message));
            }

            path = ToRelativeMessagingPath(page.NextPageUri);
        }

        return results;
    }

    private async Task<SmsMessageResult> SendFormAsync(
        HttpMethod method,
        string path,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var request = new HttpRequestMessage(method, path)
        {
            Content = new FormUrlEncodedContent(form)
        };
        ApplyBasicAuth(request);

        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToException(response, payload);
        }

        return TwilioResponseParser.ToMessageResult(JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions));
    }

    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private void ApplyBasicAuth(HttpRequestMessage request)
    {
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_options.AccountSid}:{_options.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    private static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static string TrimLeadingSlash(string path) => path.StartsWith('/') ? path[1..] : path;

    private static string? ToRelativeMessagingPath(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return absolute.PathAndQuery.TrimStart('/');
        }

        return TrimLeadingSlash(nextPageUri);
    }
}
