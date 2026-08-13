using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// <see cref="ISmsProvider"/> implemented against the Twilio messaging API (api.twilio.com,
/// <c>2010-04-01</c>) exactly as described by the <c>twilio_api_v2010</c> OpenAPI document: the
/// Messages resource for create / fetch / list / update. The <see cref="HttpClient"/> is configured
/// (base address, Basic auth, no request logging) by <see cref="TwilioServiceCollectionExtensions"/>.
/// </summary>
public class TwilioMessagingClient : ISmsProvider
{
    /// <summary>Bound to the maximum page size the spec allows, to minimise round-trips during reconciliation.</summary>
    private const int MaxPageSize = 1000;

    /// <summary>Backstop so a misbehaving next-page chain can never loop forever.</summary>
    private const int MaxReconciliationPages = 200;

    private readonly HttpClient _httpClient;
    private readonly TwilioOptions _options;

    public TwilioMessagingClient(HttpClient httpClient, TwilioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<SmsMessage> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body
        };

        if (request.SendAt.HasValue)
        {
            // Scheduling requires a Messaging Service and ScheduleType=fixed per the spec; Twilio picks
            // the sender from the service's pool, so no From is supplied.
            if (string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            {
                throw new InvalidOperationException("A Twilio MessagingServiceSid is required to schedule a message.");
            }

            form["MessagingServiceSid"] = _options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }
        else
        {
            // Immediate sends go out from the application's own configured number, which is also what the
            // reconciliation report filters on.
            form["From"] = _options.FromNumber;
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessagesResourcePath(), content, cancellationToken);
        var message = await ReadMessageOrThrowAsync(response, cancellationToken);
        return ToSmsMessage(message);
    }

    public async Task<SmsMessage> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(MessageInstancePath(providerMessageSid), cancellationToken);
        var message = await ReadMessageOrThrowAsync(response, cancellationToken);
        return ToSmsMessage(message);
    }

    public async Task CancelScheduledMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Per the spec, POST-ing Status=canceled to the message instance calls off a not-yet-sent message.
        var form = new Dictionary<string, string> { ["Status"] = "canceled" };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageInstancePath(providerMessageSid), content, cancellationToken);
        await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Per the spec, POST-ing an empty Body redacts the message text at the provider while the record survives.
        var form = new Dictionary<string, string> { ["Body"] = string.Empty };
        using var content = new FormUrlEncodedContent(form);
        using var response = await _httpClient.PostAsync(MessageInstancePath(providerMessageSid), content, cancellationToken);
        await ReadMessageOrThrowAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessage>> ListOutboundFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();

        // Ask the provider for this application's own sender, bounded by DateSent at day granularity (the
        // finest the spec's DateSent filter offers). The exact window is applied client-side below.
        var query = new List<string>
        {
            $"From={Uri.EscapeDataString(_options.FromNumber)}",
            $"DateSent%3E={Uri.EscapeDataString(fromUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            $"DateSent%3C={Uri.EscapeDataString(toUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            $"PageSize={MaxPageSize}"
        };
        var nextPath = $"{MessagesResourcePath()}?{string.Join("&", query)}";

        var results = new List<SmsMessage>();
        var pages = 0;
        while (!string.IsNullOrEmpty(nextPath) && pages < MaxReconciliationPages)
        {
            pages++;
            using var response = await _httpClient.GetAsync(nextPath, cancellationToken);
            var page = await ReadOrThrowAsync<TwilioMessageListResponse>(response, cancellationToken);

            foreach (var message in page.Messages)
            {
                var mapped = ToSmsMessage(message);
                // Refine the coarse day filter to the exact [from, to] window so the report covers the range precisely.
                if (mapped.DateSent is { } sent && (sent < fromUtc || sent > toUtc))
                {
                    continue;
                }

                results.Add(mapped);
            }

            nextPath = page.NextPageUri;
        }

        return results;
    }

    private string MessagesResourcePath() =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages.json";

    private string MessageInstancePath(string sid) =>
        $"2010-04-01/Accounts/{_options.AccountSid}/Messages/{Uri.EscapeDataString(sid)}.json";

    private static SmsMessage ToSmsMessage(TwilioMessageResource resource) => new()
    {
        Sid = resource.Sid ?? string.Empty,
        Status = resource.Status,
        ErrorCode = resource.ErrorCode,
        ErrorMessage = resource.ErrorMessage,
        From = resource.From,
        To = resource.To,
        Body = resource.Body,
        DateSent = ParseRfc2822(resource.DateSent)
    };

    private static async Task<TwilioMessageResource> ReadMessageOrThrowAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        await ReadOrThrowAsync<TwilioMessageResource>(response, cancellationToken);

    private static async Task<T> ReadOrThrowAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, cancellationToken);
        }

        var value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        if (value == null)
        {
            throw new TwilioApiException(response.StatusCode, null, "Twilio returned an empty response body.", null);
        }

        return value;
    }

    private static async Task<TwilioApiException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        TwilioErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<TwilioErrorResponse>(cancellationToken: cancellationToken);
        }
        catch
        {
            // Non-JSON error body — fall back to status only.
        }

        return new TwilioApiException(response.StatusCode, error?.Code, error?.Message, error?.MoreInfo);
    }

    private static DateTimeOffset? ParseRfc2822(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? parsed
            : null;
    }
}
