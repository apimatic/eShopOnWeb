using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioMessageClient : ITwilioMessageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessageClient> _logger;

    public TwilioMessageClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessageClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
        => CreateMessageAsync(to, body, sendAt: null, cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => CreateMessageAsync(to, body, sendAt, cancellationToken);

    public async Task<ProviderMessage?> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, TwilioHttp.MessageInstanceUrl(_options, messageSid));
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if ((int)response.StatusCode == 404)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioRequestException((int)response.StatusCode, TryReadError(payload)?.Code);
        }

        return DeserializeMessage(payload);
    }

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
        => UpdateMessageAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var url = BuildListUrl(fromNumber, fromInclusive, toInclusive);

        while (!string.IsNullOrWhiteSpace(url))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new TwilioRequestException((int)response.StatusCode, TryReadError(payload)?.Code);
            }

            var page = JsonSerializer.Deserialize<TwilioMessageListJson>(payload, JsonOptions)
                       ?? new TwilioMessageListJson();

            foreach (var message in page.Messages)
            {
                results.Add(message.ToProviderMessage());
            }

            url = ResolveNextPageUrl(page.NextPageUri);
        }

        return results;
    }

    private async Task<ProviderMessage> CreateMessageAsync(
        string to,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["From"] = _options.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            fields["MessagingServiceSid"] = _options.MessagingServiceSid;
        }

        if (sendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
            }

            fields["ScheduleType"] = "fixed";
            fields["SendAt"] = sendAt.Value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TwilioHttp.MessagesCollectionUrl(_options));
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadError(payload);
            _logger.LogWarning("Twilio create message failed with HTTP {StatusCode}, code {TwilioCode}.",
                (int)response.StatusCode, error?.Code);
            throw new TwilioRequestException((int)response.StatusCode, error?.Code);
        }

        return DeserializeMessage(payload);
    }

    private async Task<ProviderMessage> UpdateMessageAsync(
        string messageSid,
        IDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TwilioHttp.MessageInstanceUrl(_options, messageSid));
        request.Headers.Authorization = TwilioHttp.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
        request.Content = new FormUrlEncodedContent(fields);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new TwilioRequestException((int)response.StatusCode, TryReadError(payload)?.Code);
        }

        return DeserializeMessage(payload);
    }

    private string BuildListUrl(string fromNumber, DateTimeOffset fromInclusive, DateTimeOffset toInclusive)
    {
        var fromIso = fromInclusive.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var toIso = toInclusive.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        return TwilioHttp.MessagesCollectionUrl(_options)
               + "?From=" + Uri.EscapeDataString(fromNumber)
               + "&" + Uri.EscapeDataString("DateSent>") + "=" + Uri.EscapeDataString(fromIso)
               + "&" + Uri.EscapeDataString("DateSent<") + "=" + Uri.EscapeDataString(toIso)
               + "&PageSize=1000";
    }

    private string? ResolveNextPageUrl(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return TwilioHttp.MessagingRoot(_options) + absolute.PathAndQuery;
        }

        return TwilioHttp.MessagingRoot(_options) + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri);
    }

    private static ProviderMessage DeserializeMessage(string payload)
    {
        var json = JsonSerializer.Deserialize<TwilioMessageJson>(payload, JsonOptions)
                   ?? throw new InvalidOperationException("Twilio returned an empty message payload.");
        return json.ToProviderMessage();
    }

    private static TwilioErrorJson? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioErrorJson>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
