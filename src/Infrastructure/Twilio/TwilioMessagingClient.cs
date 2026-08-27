using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Hand-written client for the Twilio messaging API, built against
/// api-specs/twilio/twilio_api_v2010/twilio_api_v2010.yaml:
///   POST /2010-04-01/Accounts/{AccountSid}/Messages.json        (CreateMessage)
///   GET  /2010-04-01/Accounts/{AccountSid}/Messages.json        (ListMessage)
///   GET  /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  (FetchMessage)
///   POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json  (UpdateMessage: cancel / redact)
/// Auth: HTTP basic with AccountSid:AuthToken (spec securityScheme accountSid_authToken).
/// </summary>
public class TwilioMessagingClient : IMessagingClient
{
    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingClient> _logger;

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options, ILogger<TwilioMessagingClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.AccountSid) || string.IsNullOrWhiteSpace(_options.AuthToken))
        {
            throw new InvalidOperationException(
                "Twilio is not configured: set Twilio:AccountSid and Twilio:AuthToken (e.g. via user-secrets).");
        }
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
        {
            throw new InvalidOperationException("Twilio is not configured: set Twilio:FromNumber.");
        }
    }

    private string MessagesUrl => $"/2010-04-01/Accounts/{_options.AccountSid}/Messages.json";
    private string MessageUrl(string sid) => $"/2010-04-01/Accounts/{_options.AccountSid}/Messages/{sid}.json";

    public async Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["From"] = _options.FromNumber,
            ["Body"] = body
        };
        return await PostForMessageAsync(MessagesUrl, form, "CreateMessage", cancellationToken);
    }

    public async Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new InvalidOperationException(
                "Twilio is not configured: scheduled messages require Twilio:MessagingServiceSid.");
        }

        var form = new Dictionary<string, string>
        {
            ["To"] = to,
            ["MessagingServiceSid"] = _options.MessagingServiceSid,
            ["Body"] = body,
            ["ScheduleType"] = "fixed",
            ["SendAt"] = sendAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)
        };
        return await PostForMessageAsync(MessagesUrl, form, "CreateMessage(scheduled)", cancellationToken);
    }

    public async Task<ProviderMessage> FetchMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageUrl(messageSid), cancellationToken);
        return await ReadMessageAsync(response, "FetchMessage", cancellationToken);
    }

    public async Task<ProviderMessage> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        return await PostForMessageAsync(MessageUrl(messageSid), form, "UpdateMessage(cancel)", cancellationToken);
    }

    public async Task<ProviderMessage> RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        return await PostForMessageAsync(MessageUrl(messageSid), form, "UpdateMessage(redact)", cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        // Ask the provider for this application's own sending number's messages only.
        // DateSent filters accept GMT date-times ("2019-06-11 22:05:25.000" per the spec examples).
        const string dateFormat = "yyyy-MM-dd HH:mm:ss";
        var query = string.Join("&", new[]
        {
            $"From={Uri.EscapeDataString(_options.FromNumber)}",
            $"{Uri.EscapeDataString("DateSent>=")}={Uri.EscapeDataString(fromUtc.UtcDateTime.ToString(dateFormat, CultureInfo.InvariantCulture))}",
            $"{Uri.EscapeDataString("DateSent<=")}={Uri.EscapeDataString(toUtc.UtcDateTime.ToString(dateFormat, CultureInfo.InvariantCulture))}",
            "PageSize=1000"
        });

        var results = new List<ProviderMessage>();
        string? next = $"{MessagesUrl}?{query}";
        while (next is not null)
        {
            using var response = await _httpClient.GetAsync(next, cancellationToken);
            await EnsureSuccessAsync(response, "ListMessage", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioListMessagesResponse>(cancellationToken: cancellationToken);
            if (page is not null)
            {
                results.AddRange(page.Messages.Select(Map));
                next = page.NextPageUri;
            }
            else
            {
                next = null;
            }
        }
        return results;
    }

    private async Task<ProviderMessage> PostForMessageAsync(string url, Dictionary<string, string> form, string operation, CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        return await ReadMessageAsync(response, operation, cancellationToken);
    }

    private static async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, operation, cancellationToken);
        var resource = await response.Content.ReadFromJsonAsync<TwilioMessageResource>(cancellationToken: cancellationToken);
        if (resource?.Sid is null)
        {
            throw new MessagingProviderException((int)response.StatusCode, null, operation);
        }
        return Map(resource);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string operation, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int? providerCode = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<TwilioErrorResource>(cancellationToken: cancellationToken);
            providerCode = error?.Code;
        }
        catch
        {
            // Error body wasn't the standard Twilio error payload; the HTTP status is enough.
        }
        // Note: the provider's error message can embed destination numbers, so it is not propagated.
        throw new MessagingProviderException((int)response.StatusCode, providerCode, operation);
    }

    private static ProviderMessage Map(TwilioMessageResource resource) => new(
        resource.Sid ?? string.Empty,
        resource.Status,
        resource.To,
        resource.From,
        resource.Body,
        resource.ErrorCode,
        resource.ErrorMessage,
        TwilioMessageResource.ParseRfc2822(resource.DateCreated),
        TwilioMessageResource.ParseRfc2822(resource.DateSent));
}
