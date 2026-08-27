using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioOptions> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public async Task<ProviderMessageResult> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["From"] = _options.FromNumber,
            ["Body"] = request.Body
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new TwilioClientException("Scheduling a message requires Twilio:MessagingServiceSid.");
            }

            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        var uri = BuildMessagingUri($"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json");
        using var response = await SendFormAsync(HttpMethod.Post, uri, fields, retryServerErrors: false, "CreateMessage", cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, "CreateMessage");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Map(TwilioHttp.Deserialize<TwilioMessageResource>(json));
    }

    public async Task<ProviderMessageResult> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var uri = BuildMessagingUri($"/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendAsync(HttpMethod.Get, uri, retryServerErrors: true, "FetchMessage", cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, "FetchMessage");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Map(TwilioHttp.Deserialize<TwilioMessageResource>(json));
    }

    public Task<ProviderMessageResult> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, "CancelMessage", cancellationToken);
    }

    public Task<ProviderMessageResult> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, "RedactMessage", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageResult>> ListSentFromAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, ProviderMessageResult>(StringComparer.OrdinalIgnoreCase);
        var startDate = from.UtcDateTime.Date;
        var endDate = to.UtcDateTime.Date;
        if (endDate < startDate)
        {
            (startDate, endDate) = (endDate, startDate);
        }

        for (var day = startDate; day <= endDate; day = day.AddDays(1))
        {
            var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var query = new List<string>
            {
                "From=" + Uri.EscapeDataString(_options.FromNumber),
                "DateSent=" + Uri.EscapeDataString(date),
                "PageSize=1000"
            };

            var next = $"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json?{string.Join("&", query)}";
            while (!string.IsNullOrWhiteSpace(next))
            {
                var uri = BuildMessagingUri(next);
                using var response = await SendAsync(HttpMethod.Get, uri, retryServerErrors: true, "ListMessage", cancellationToken);
                await TwilioHttp.EnsureSuccessAsync(response, "ListMessage");
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var page = TwilioHttp.Deserialize<TwilioMessageListResponse>(json);
                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages.Select(Map))
                    {
                        if (!string.IsNullOrWhiteSpace(message.Sid))
                        {
                            results[message.Sid] = message;
                        }
                    }
                }

                next = page.NextPageUri;
            }
        }

        return results.Values.ToList();
    }

    private async Task<ProviderMessageResult> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> fields,
        string operation,
        CancellationToken cancellationToken)
    {
        var uri = BuildMessagingUri($"/2010-04-01/Accounts/{_options.AccountSid}/Messages/{messageSid}.json");
        using var response = await SendFormAsync(HttpMethod.Post, uri, fields, retryServerErrors: true, operation, cancellationToken);
        await TwilioHttp.EnsureSuccessAsync(response, operation);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return Map(TwilioHttp.Deserialize<TwilioMessageResource>(json));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        Uri uri,
        bool retryServerErrors,
        string operation,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await TwilioHttp.SendWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(method, uri);
                request.Headers.Authorization = CreateAuthHeader();
                return request;
            },
            retryServerErrors,
            _logger,
            operation,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendFormAsync(
        HttpMethod method,
        Uri uri,
        Dictionary<string, string> fields,
        bool retryServerErrors,
        string operation,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        return await TwilioHttp.SendWithRetryAsync(
            client,
            () =>
            {
                var request = new HttpRequestMessage(method, uri);
                request.Headers.Authorization = CreateAuthHeader();
                request.Content = new FormUrlEncodedContent(fields);
                return request;
            },
            retryServerErrors,
            _logger,
            operation,
            cancellationToken);
    }

    private Uri BuildMessagingUri(string pathAndQuery)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl.TrimEnd('/');

        if (pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var absolute = new Uri(pathAndQuery);
            pathAndQuery = absolute.PathAndQuery;
        }

        if (!pathAndQuery.StartsWith('/'))
        {
            pathAndQuery = "/" + pathAndQuery;
        }

        return new Uri(baseUrl + pathAndQuery);
    }

    private AuthenticationHeaderValue CreateAuthHeader()
    {
        var raw = $"{_options.AccountSid}:{_options.AuthToken}";
        return new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(raw)));
    }

    private static ProviderMessageResult Map(TwilioMessageResource resource)
    {
        return new ProviderMessageResult(
            resource.Sid,
            resource.Status ?? "unknown",
            resource.Body,
            resource.From,
            resource.To,
            resource.ErrorCode,
            resource.ErrorMessage,
            ParseTwilioDate(resource.DateCreated),
            ParseTwilioDate(resource.DateSent));
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "null")
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private sealed class TwilioMessageResource
    {
        [JsonPropertyName("sid")]
        public string? Sid { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string? To { get; set; }

        [JsonPropertyName("error_code")]
        public int? ErrorCode { get; set; }

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("date_created")]
        public string? DateCreated { get; set; }

        [JsonPropertyName("date_sent")]
        public string? DateSent { get; set; }
    }

    private sealed class TwilioMessageListResponse
    {
        [JsonPropertyName("messages")]
        public List<TwilioMessageResource>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
