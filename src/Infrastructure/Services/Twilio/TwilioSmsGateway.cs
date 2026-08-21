using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioSmsGateway : ISmsGateway
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<TwilioSettings> _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        IHttpClientFactory httpClientFactory,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsGateway> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _logger = logger;
    }

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Value;
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = settings.FromNumber
        };

        if (!string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
        {
            form["MessagingServiceSid"] = settings.MessagingServiceSid;
        }

        if (request.SendAt.HasValue)
        {
            if (string.IsNullOrWhiteSpace(settings.MessagingServiceSid))
            {
                throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
            }

            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, ToMessagingRequestUri(settings, MessagesCollectionPath(settings)))
        {
            Content = new FormUrlEncodedContent(form)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        var resource = await SendForResourceAsync(message, expectCreated: true, cancellationToken)
            ?? throw new InvalidOperationException("The messaging provider returned an empty message resource.");
        return ToSnapshot(resource);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var settings = _settings.Value;
        using var message = new HttpRequestMessage(HttpMethod.Get, ToMessagingRequestUri(settings, MessageInstancePath(settings, providerMessageSid)));
        var resource = await SendForResourceAsync(message, expectCreated: false, cancellationToken, allowNotFound: true);
        return resource is null ? null : ToSnapshot(resource);
    }

    public async Task<SmsMessageSnapshot?> CancelAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        return await UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        return await UpdateMessageAsync(providerMessageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var settings = _settings.Value;
        var results = new List<SmsMessageSnapshot>();
        var pageSize = 1000;
        var relative = MessagesCollectionPath(settings)
            + "?From=" + Uri.EscapeDataString(fromNumber)
            + "&DateSent%3E=" + Uri.EscapeDataString(from.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))
            + "&DateSent%3C=" + Uri.EscapeDataString(to.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))
            + "&PageSize=" + pageSize.ToString(CultureInfo.InvariantCulture);

        var pages = 0;
        while (!string.IsNullOrEmpty(relative) && pages < 100)
        {
            pages++;
            using var message = new HttpRequestMessage(HttpMethod.Get, ToMessagingRequestUri(settings, relative));
            TwilioHttp.ApplyBasicAuth(message, _settings);

            var client = _httpClientFactory.CreateClient(TwilioHttp.MessagingClientName);
            using var response = await client.SendAsync(message, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            EnsureSuccess(response, payload);

            var page = JsonSerializer.Deserialize<TwilioMessageListResponse>(payload, JsonOptions);
            if (page?.Messages is not null)
            {
                foreach (var item in page.Messages)
                {
                    results.Add(ToSnapshot(item));
                }
            }

            relative = page?.NextPageUri;
        }

        return results;
    }

    private async Task<SmsMessageSnapshot?> UpdateMessageAsync(
        string providerMessageSid,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        var settings = _settings.Value;
        using var message = new HttpRequestMessage(HttpMethod.Post, ToMessagingRequestUri(settings, MessageInstancePath(settings, providerMessageSid)))
        {
            Content = new FormUrlEncodedContent(form)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
        var resource = await SendForResourceAsync(message, expectCreated: false, cancellationToken, allowNotFound: true);
        return resource is null ? null : ToSnapshot(resource);
    }

    private async Task<TwilioMessageResource?> SendForResourceAsync(
        HttpRequestMessage request,
        bool expectCreated,
        CancellationToken cancellationToken,
        bool allowNotFound = false)
    {
        TwilioHttp.ApplyBasicAuth(request, _settings);
        var client = _httpClientFactory.CreateClient(TwilioHttp.MessagingClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (expectCreated && response.StatusCode != HttpStatusCode.Created && !response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response, payload);
        }

        if (!expectCreated && !response.IsSuccessStatusCode)
        {
            throw CreateProviderException(response, payload);
        }

        var resource = JsonSerializer.Deserialize<TwilioMessageResource>(payload, JsonOptions);
        if (resource is null || string.IsNullOrEmpty(resource.Sid))
        {
            throw new InvalidOperationException("The messaging provider returned an empty message resource.");
        }

        return resource;
    }

    private Exception CreateProviderException(HttpResponseMessage response, string payload)
    {
        var error = TryReadError(payload);
        var code = error?.Code?.ToString() ?? ((int)response.StatusCode).ToString();
        _logger.LogWarning("Twilio messaging API returned status {StatusCode} with error code {ErrorCode}.", (int)response.StatusCode, code);
        return new InvalidOperationException($"Twilio messaging API failed with status {(int)response.StatusCode} (code {code}).");
    }

    private static void EnsureSuccess(HttpResponseMessage response, string payload)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = TryReadError(payload);
        var code = error?.Code?.ToString() ?? ((int)response.StatusCode).ToString();
        throw new InvalidOperationException($"Twilio messaging API failed with status {(int)response.StatusCode} (code {code}).");
    }

    private static TwilioRestError? TryReadError(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<TwilioRestError>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string MessagesCollectionPath(TwilioSettings settings) =>
        $"/2010-04-01/Accounts/{settings.AccountSid}/Messages.json";

    private static string MessageInstancePath(TwilioSettings settings, string sid) =>
        $"/2010-04-01/Accounts/{settings.AccountSid}/Messages/{sid}.json";

    private static string ToMessagingRequestUri(TwilioSettings settings, string nextPageUri)
    {
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            return TwilioHttp.MessagingBaseUrl(settings) + absolute.PathAndQuery;
        }

        if (nextPageUri.StartsWith('/'))
        {
            return TwilioHttp.MessagingBaseUrl(settings) + nextPageUri;
        }

        return TwilioHttp.MessagingBaseUrl(settings) + "/" + nextPageUri;
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new SmsMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.Status ?? "unknown",
            resource.From,
            resource.To,
            resource.Body,
            resource.ErrorCode,
            resource.ErrorMessage,
            ParseTwilioDate(resource.DateSent),
            ParseTwilioDate(resource.DateCreated));
    }

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return parsed;
        }

        return null;
    }
}
