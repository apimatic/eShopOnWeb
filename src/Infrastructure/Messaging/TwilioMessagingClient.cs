using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<TwilioOptions> _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.Value.FromNumber;

    public async Task<ProviderMessageState> CreateMessageAsync(OutboundSmsRequest request, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio MessagingServiceSid is required to schedule a message.");
            }

            form["MessagingServiceSid"] = options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else
        {
            var from = request.From ?? options.FromNumber;
            if (string.IsNullOrWhiteSpace(from))
            {
                throw new InvalidOperationException("Twilio FromNumber must be configured to send a message.");
            }

            form["From"] = from;

            if (!string.IsNullOrWhiteSpace(options.MessagingServiceSid))
            {
                form["MessagingServiceSid"] = options.MessagingServiceSid;
            }
        }

        using var response = await SendAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessagesCollectionUri(options))
                {
                    Content = new FormUrlEncodedContent(form)
                };
                httpRequest.Headers.Authorization = TwilioHttp.CreateBasicAuth(options);
                return httpRequest;
            },
            retryNotFound: false,
            cancellationToken).ConfigureAwait(false);

        await TwilioHttp.EnsureSuccessAsync(response).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Map(DeserializeMessage(payload));
    }

    public async Task<ProviderMessageState> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        using var response = await SendAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Get, MessageInstanceUri(options, messageSid));
                httpRequest.Headers.Authorization = TwilioHttp.CreateBasicAuth(options);
                return httpRequest;
            },
            retryNotFound: true,
            cancellationToken).ConfigureAwait(false);

        await TwilioHttp.EnsureSuccessAsync(response).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Map(DeserializeMessage(payload));
    }

    public Task<ProviderMessageState> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public Task<ProviderMessageState> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessageState>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var options = _options.Value;
        var results = new List<ProviderMessageState>();
        var next = BuildListUrl(options, fromNumber, from, to);

        while (!string.IsNullOrEmpty(next))
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, next);
            httpRequest.Headers.Authorization = TwilioHttp.CreateBasicAuth(options);

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            await TwilioHttp.EnsureSuccessAsync(response).ConfigureAwait(false);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var page = JsonSerializer.Deserialize<ListMessageResponseDto>(payload, TwilioHttp.JsonOptions)
                ?? new ListMessageResponseDto();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(Map(message));
                }
            }

            next = ResolveNextPage(options, page.NextPageUri);
        }

        _logger.LogInformation(
            "Listed {Count} provider messages for the configured sending number in the requested range.",
            results.Count);

        return results;
    }

    private async Task<ProviderMessageState> UpdateMessageAsync(
        string messageSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;
        using var response = await SendAsync(
            () =>
            {
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, MessageInstanceUri(options, messageSid))
                {
                    Content = new FormUrlEncodedContent(form)
                };
                httpRequest.Headers.Authorization = TwilioHttp.CreateBasicAuth(options);
                return httpRequest;
            },
            retryNotFound: true,
            cancellationToken).ConfigureAwait(false);

        await TwilioHttp.EnsureSuccessAsync(response).ConfigureAwait(false);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return Map(DeserializeMessage(payload));
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        bool retryNotFound,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        const int maxAttempts = 4;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            response?.Dispose();
            var request = requestFactory();
            try
            {
                response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                request.Dispose();
            }

            if (!retryNotFound || response.StatusCode != System.Net.HttpStatusCode.NotFound || attempt == maxAttempts)
            {
                return response;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
        }

        return response!;
    }

    private static string MessagesCollectionUri(TwilioOptions options)
        => $"{TwilioHttp.MessagingBaseUrl(options)}/2010-04-01/Accounts/{Uri.EscapeDataString(options.AccountSid)}/Messages.json";

    private static string MessageInstanceUri(TwilioOptions options, string messageSid)
        => $"{TwilioHttp.MessagingBaseUrl(options)}/2010-04-01/Accounts/{Uri.EscapeDataString(options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json";

    private static string BuildListUrl(TwilioOptions options, string fromNumber, DateTimeOffset from, DateTimeOffset to)
    {
        var builder = new UriBuilder(MessagesCollectionUri(options));
        var query = new List<string>
        {
            "From=" + Uri.EscapeDataString(fromNumber),
            "DateSent%3E=" + Uri.EscapeDataString(from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            "DateSent%3C=" + Uri.EscapeDataString(to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            "PageSize=1000"
        };
        builder.Query = string.Join("&", query);
        return builder.Uri.ToString();
    }

    private static string? ResolveNextPage(TwilioOptions options, string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var baseUrl = new Uri(TwilioHttp.MessagingBaseUrl(options) + "/");
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return new Uri(baseUrl, absolute.PathAndQuery).ToString();
        }

        return new Uri(baseUrl, nextPageUri.TrimStart('/')).ToString();
    }

    private static MessageResourceDto DeserializeMessage(string payload)
        => JsonSerializer.Deserialize<MessageResourceDto>(payload, TwilioHttp.JsonOptions)
           ?? throw new InvalidOperationException("Twilio returned an empty message resource.");

    private static ProviderMessageState Map(MessageResourceDto dto)
        => new(
            dto.Sid ?? string.Empty,
            dto.Status ?? "unknown",
            dto.ErrorCode,
            PiiRedactor.Redact(dto.ErrorMessage),
            TwilioHttp.ParseRfc2822(dto.DateSent),
            TwilioHttp.ParseRfc2822(dto.DateCreated),
            dto.Body,
            dto.From);

    private sealed class MessageResourceDto
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public int? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
        public string? Body { get; set; }

        [JsonPropertyName("from")]
        public string? From { get; set; }
    }

    private sealed class ListMessageResponseDto
    {
        public List<MessageResourceDto>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }
}
