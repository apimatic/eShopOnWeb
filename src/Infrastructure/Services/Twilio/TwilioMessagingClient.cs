using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Messaging API (api.twilio.com /2010-04-01 Messages) from twilio_api_v2010.yaml:
/// CreateMessage, FetchMessage, UpdateMessage, ListMessage.
/// Twilio:BaseUrl, when set, is used verbatim as the base address for every call.
/// </summary>
public sealed class TwilioMessagingClient : ISmsGateway
{
    public const string HttpClientName = "TwilioMessaging";
    internal const string DefaultBaseUrl = "https://api.twilio.com";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioOptions> _options;

    public TwilioMessagingClient(IHttpClientFactory httpClientFactory, IOptions<TwilioOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public Task<SmsMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("From", settings.FromNumber),
            new("Body", body)
        };
        return CreateMessageAsync(fields, cancellationToken);
    }

    public Task<SmsMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", toE164),
            new("Body", body),
            new("MessagingServiceSid", settings.MessagingServiceSid),
            new("ScheduleType", "fixed"),
            new("SendAt", sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture))
        };

        return CreateMessageAsync(fields, cancellationToken);
    }

    public async Task<SmsMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var uri = MessageInstancePath(providerMessageSid);
        using var response = await client.GetAsync(uri, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToApiException((int)response.StatusCode, payload);
        }

        return TwilioResponseParser.ToSmsMessage(TwilioResponseParser.Deserialize<TwilioMessageResource>(payload, JsonOptions));
    }

    public Task<SmsMessage> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);

    public Task<SmsMessage> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);

    public async Task<IReadOnlyList<SmsMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken = default)
    {
        var settings = _options.Value;
        var results = new List<SmsMessage>();
        var fromIso = fromInclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var toIso = toInclusive.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var query = string.Join("&", new[]
        {
            $"{Uri.EscapeDataString("From")}={Uri.EscapeDataString(settings.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>")}={Uri.EscapeDataString(fromIso)}",
            $"{Uri.EscapeDataString("DateSent<")}={Uri.EscapeDataString(toIso)}",
            "PageSize=1000"
        });

        var next = MessagesListPath() + "?" + query;
        var client = _httpClientFactory.CreateClient(HttpClientName);

        while (!string.IsNullOrEmpty(next))
        {
            using var response = await client.GetAsync(ResolveMessagingUri(next), cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw TwilioResponseParser.ToApiException((int)response.StatusCode, payload);
            }

            var page = TwilioResponseParser.Deserialize<TwilioListMessageResponse>(payload, JsonOptions);
            if (page.Messages is not null)
            {
                results.AddRange(page.Messages.Select(TwilioResponseParser.ToSmsMessage));
            }

            next = string.IsNullOrEmpty(page.NextPageUri) ? null : page.NextPageUri;
        }

        return results;
    }

    private async Task<SmsMessage> CreateMessageAsync(List<KeyValuePair<string, string>> fields, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        using var content = new FormUrlEncodedContent(fields);
        using var response = await client.PostAsync(MessagesListPath(), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToApiException((int)response.StatusCode, payload);
        }

        return TwilioResponseParser.ToSmsMessage(TwilioResponseParser.Deserialize<TwilioMessageResource>(payload, JsonOptions));
    }

    private async Task<SmsMessage> UpdateMessageAsync(string sid, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        // Encode explicitly so an empty Body (message redaction) is still sent as "Body=".
        var encoded = string.Join("&", fields.Select(f =>
            $"{Uri.EscapeDataString(f.Key)}={Uri.EscapeDataString(f.Value)}"));
        using var content = new StringContent(encoded, Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await client.PostAsync(MessageInstancePath(sid), content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw TwilioResponseParser.ToApiException((int)response.StatusCode, payload);
        }

        return TwilioResponseParser.ToSmsMessage(TwilioResponseParser.Deserialize<TwilioMessageResource>(payload, JsonOptions));
    }

    private string MessagesListPath()
    {
        var accountSid = _options.Value.AccountSid;
        return $"2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages.json";
    }

    private string MessageInstancePath(string sid)
    {
        var accountSid = _options.Value.AccountSid;
        return $"2010-04-01/Accounts/{Uri.EscapeDataString(accountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";
    }

    private Uri ResolveMessagingUri(string uriOrPath)
    {
        var configured = string.IsNullOrWhiteSpace(_options.Value.BaseUrl)
            ? DefaultBaseUrl
            : _options.Value.BaseUrl.TrimEnd('/');

        var root = new Uri(EnsureTrailingSlash(configured));
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            return new Uri(root, absolute.PathAndQuery.TrimStart('/'));
        }

        return new Uri(root, uriOrPath.TrimStart('/'));
    }

    private static string EnsureTrailingSlash(string value) => value.EndsWith("/") ? value : value + "/";
}
