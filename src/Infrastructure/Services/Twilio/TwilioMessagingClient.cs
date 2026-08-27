using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Messaging API (api.v2010) — CreateMessage, ListMessage, FetchMessage, UpdateMessage.
/// When Twilio:BaseUrl is set it is used as the base address for every call in this client.
/// </summary>
public class TwilioMessagingClient : ITwilioMessagingClient
{
    private const string DefaultMessagingBaseUrl = "https://api.twilio.com";

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.DefaultRequestHeaders.Authorization =
            TwilioJson.CreateBasicAuth(_options.AccountSid, _options.AuthToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var fields = ImmediateFields(to, body);
        return PostMessageAsync(fields, cancellationToken);
    }

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid is required to queue a follow-up with the provider.");
        }

        var fields = new Dictionary<string, string>
        {
            ["To"] = to,
            ["Body"] = body,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };

        if (!string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            fields["From"] = _options.FromNumber;
        }

        return PostMessageAsync(fields, cancellationToken);
    }

    public async Task<ProviderMessage> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioResponseGuard.ThrowIfFailed((int)response.StatusCode, content);
        return Map(TwilioJson.Read<TwilioMessageResourceBody>(content));
    }

    public Task<ProviderMessage> CancelAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return PostMessageInstanceAsync(messageSid, new Dictionary<string, string> { ["Status"] = "canceled" }, cancellationToken);
    }

    public Task<ProviderMessage> RedactBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        return PostMessageInstanceAsync(messageSid, new Dictionary<string, string> { ["Body"] = string.Empty }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is required to reconcile messages.");
        }

        var results = new List<ProviderMessage>();
        var fromValue = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var toValue = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var path =
            $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json" +
            $"?From={Uri.EscapeDataString(_options.FromNumber)}" +
            $"&DateSent%3E={Uri.EscapeDataString(fromValue)}" +
            $"&DateSent%3C={Uri.EscapeDataString(toValue)}" +
            "&PageSize=1000";

        while (!string.IsNullOrEmpty(path))
        {
            var uri = MessagingUri(path);
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            TwilioResponseGuard.ThrowIfFailed((int)response.StatusCode, content);
            var page = TwilioJson.Read<TwilioMessageListBody>(content);
            foreach (var message in page.Messages)
            {
                results.Add(Map(message));
            }

            path = page.NextPageUri;
        }

        return results;
    }

    private Dictionary<string, string> ImmediateFields(string to, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:FromNumber is required to send messages.");
        }

        return new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
    }

    private async Task<ProviderMessage> PostMessageAsync(Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages.json");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioResponseGuard.ThrowIfFailed((int)response.StatusCode, content);
        return Map(TwilioJson.Read<TwilioMessageResourceBody>(content));
    }

    private async Task<ProviderMessage> PostMessageInstanceAsync(
        string messageSid,
        Dictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        var uri = MessagingUri($"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages/{Uri.EscapeDataString(messageSid)}.json");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new FormUrlEncodedContent(fields)
        };
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        TwilioResponseGuard.ThrowIfFailed((int)response.StatusCode, content);
        return Map(TwilioJson.Read<TwilioMessageResourceBody>(content));
    }

    private Uri MessagingUri(string relativeOrAbsolutePath)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? DefaultMessagingBaseUrl
            : _options.BaseUrl;
        var normalizedBase = baseUrl.TrimEnd('/') + "/";

        if (relativeOrAbsolutePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || relativeOrAbsolutePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var providerUri = new Uri(relativeOrAbsolutePath, UriKind.Absolute);
            return new Uri(new Uri(normalizedBase, UriKind.Absolute), providerUri.PathAndQuery.TrimStart('/'));
        }

        return new Uri(new Uri(normalizedBase, UriKind.Absolute), relativeOrAbsolutePath.TrimStart('/'));
    }

    private static ProviderMessage Map(TwilioMessageResourceBody body) =>
        new(
            body.Sid,
            body.Status,
            body.Body,
            body.ErrorCode,
            body.ErrorMessage,
            TwilioJson.ParseRfc2822(body.DateSent),
            TwilioJson.ParseRfc2822(body.DateCreated),
            body.From);
}
