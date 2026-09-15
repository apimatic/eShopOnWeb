using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioMessagingClient : ITwilioMessagingClient
{
    public const string HttpClientName = "TwilioMessaging";
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> options,
        ILogger<TwilioMessagingClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = options.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<TwilioMessageResult> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt = null,
        CancellationToken cancellationToken = default)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["From"] = _settings.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _settings.MessagingServiceSid;
        }

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Scheduled messages require Twilio:MessagingServiceSid.");
            }

            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = sendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        }

        var response = await SendFormAsync(
            HttpMethod.Post,
            MessagesCollectionPath(),
            fields,
            cancellationToken);

        var payload = await ReadMessageAsync(response, cancellationToken);
        return ToResult(payload);
    }

    public async Task<TwilioMessageResult> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await SendFormAsync(
            HttpMethod.Get,
            MessageInstancePath(messageSid),
            fields: null,
            cancellationToken);

        var payload = await ReadMessageAsync(response, cancellationToken);
        return ToResult(payload);
    }

    public async Task<IReadOnlyList<TwilioMessageResult>> ListMessagesFromNumberAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        // DateSent> / DateSent< are GMT dates. Widen by a day on each side so an ISO-8601
        // window that starts or ends mid-day is fully covered by the provider query; From is
        // applied by the provider rather than by filtering a broader list afterwards.
        var fromDate = from.ToUniversalTime().UtcDateTime.Date.AddDays(-1).ToString("yyyy-MM-dd");
        var toDate = to.ToUniversalTime().UtcDateTime.Date.AddDays(1).ToString("yyyy-MM-dd");
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(fromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromDate)}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toDate)}",
            "PageSize=1000"
        });
        var firstPage = $"{GetMessagingBaseUrl().TrimEnd('/')}/{MessagesCollectionPath()}?{query}";

        var results = new List<TwilioMessageResult>();
        string? next = firstPage;

        while (!string.IsNullOrEmpty(next))
        {
            var response = await SendFormAsync(HttpMethod.Get, next, fields: null, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<TwilioMessageListPayload>(json, JsonOptions)
                       ?? new TwilioMessageListPayload();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    var mapped = ToResult(message);
                    if (IsInRange(mapped, from, to))
                    {
                        results.Add(mapped);
                    }
                }
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    public async Task<TwilioMessageResult> CancelMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await SendFormAsync(
            HttpMethod.Post,
            MessageInstancePath(messageSid),
            new Dictionary<string, string> { ["Status"] = "canceled" },
            cancellationToken);

        var payload = await ReadMessageAsync(response, cancellationToken);
        return ToResult(payload);
    }

    public async Task<TwilioMessageResult> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var response = await SendFormAsync(
            HttpMethod.Post,
            MessageInstancePath(messageSid),
            new Dictionary<string, string> { ["Body"] = string.Empty },
            cancellationToken);

        var payload = await ReadMessageAsync(response, cancellationToken);
        return ToResult(payload);
    }

    private static StringContent CreateFormContent(Dictionary<string, string> fields)
    {
        var encoded = string.Join("&", fields.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? string.Empty)}"));
        return new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
    }

    private string GetMessagingBaseUrl() =>
        string.IsNullOrWhiteSpace(_settings.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _settings.BaseUrl.TrimEnd('/');

    private string MessagesCollectionPath() =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages.json";

    private string MessageInstancePath(string messageSid) =>
        $"2010-04-01/Accounts/{_settings.AccountSid}/Messages/{messageSid}.json";

    private async Task<HttpResponseMessage> SendFormAsync(
        HttpMethod method,
        string relativeOrNextPage,
        Dictionary<string, string>? fields,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var requestUri = ResolveMessagingUri(relativeOrNextPage);
        var request = new HttpRequestMessage(method, requestUri);

        if (fields is not null)
        {
            request.Content = CreateFormContent(fields);
        }

        var response = await client.SendAsync(request, cancellationToken);
        return response;
    }

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        var baseUri = new Uri(GetMessagingBaseUrl().TrimEnd('/') + "/", UriKind.Absolute);

        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri(baseUri, absolute.PathAndQuery);
        }

        return new Uri(baseUri, uriOrPath.TrimStart('/'));
    }

    private async Task<TwilioMessagePayload> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TwilioMessagePayload>(json, JsonOptions)
               ?? throw new InvalidOperationException("The messaging provider returned an empty body.");
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
        var error = TryReadError(errorBody);
        var code = error?.Code?.ToString() ?? ((int)response.StatusCode).ToString();
        _logger.LogWarning("Messaging API request failed with status {Status} and provider code {Code}.", (int)response.StatusCode, code);
        throw new TwilioProviderException(
            $"Messaging API request failed with status {(int)response.StatusCode} (code {code}).");
    }

    private static TwilioErrorPayload? TryReadError(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorPayload>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TwilioMessageResult ToResult(TwilioMessagePayload payload) =>
        new(
            payload.Sid ?? string.Empty,
            payload.Status ?? string.Empty,
            payload.Body,
            payload.To,
            payload.From,
            payload.ErrorCode,
            payload.DateSent,
            payload.DateCreated);

    private static bool IsInRange(TwilioMessageResult message, DateTimeOffset from, DateTimeOffset to)
    {
        var timestamp = ParseProviderTimestamp(message.DateSent)
                        ?? ParseProviderTimestamp(message.DateCreated);
        if (timestamp is null)
        {
            return true;
        }

        return timestamp >= from && timestamp <= to;
    }

    private static DateTimeOffset? ParseProviderTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private sealed class TwilioMessagePayload
    {
        public string? Sid { get; set; }
        public string? Status { get; set; }
        public string? Body { get; set; }
        public string? To { get; set; }
        public string? From { get; set; }
        public int? ErrorCode { get; set; }
        public string? DateSent { get; set; }
        public string? DateCreated { get; set; }
    }

    private sealed class TwilioMessageListPayload
    {
        public List<TwilioMessagePayload>? Messages { get; set; }

        [JsonPropertyName("next_page_uri")]
        public string? NextPageUri { get; set; }
    }

    private sealed class TwilioErrorPayload
    {
        public int? Code { get; set; }
        public int? Status { get; set; }
    }
}

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message) : base(message)
    {
    }
}

public sealed class TwilioBasicAuthHandler : DelegatingHandler
{
    private readonly IOptions<TwilioSettings> _options;

    public TwilioBasicAuthHandler(IOptions<TwilioSettings> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return base.SendAsync(request, cancellationToken);
    }
}
