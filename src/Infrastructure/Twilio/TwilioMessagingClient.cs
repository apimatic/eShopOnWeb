using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Twilio.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Messaging API (api.v2010 Message resource) against https://api.twilio.com
/// or Twilio:BaseUrl when that override is set.
/// </summary>
public class TwilioMessagingClient : TwilioApiClientBase, ISmsGateway
{
    public const string HttpClientName = "TwilioMessaging";
    public const string DefaultBaseUrl = "https://api.twilio.com/";

    public TwilioMessagingClient(HttpClient httpClient, IOptions<TwilioOptions> options)
        : base(httpClient, options)
    {
    }

    public string ConfiguredFromNumber => Options.FromNumber;

    public async Task<SmsMessageSnapshot> SendAsync(SmsSendRequest request, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["To"] = request.To,
            ["Body"] = request.Body,
            ["From"] = Options.FromNumber
        };

        if (request.SendAt is not null)
        {
            form["MessagingServiceSid"] = Options.MessagingServiceSid;
            form["ScheduleType"] = "fixed";
            form["SendAt"] = request.SendAt.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync(MessagesCollectionPath(), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await ReadMessageAsync(response, cancellationToken);
        return ToSnapshot(payload);
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(MessageInstancePath(providerSid), cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return ToSnapshot(await ReadMessageAsync(response, cancellationToken));
    }

    public async Task<SmsMessageSnapshot?> CancelAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["Status"] = "canceled"
        };
        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync(MessageInstancePath(providerSid), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return ToSnapshot(await ReadMessageAsync(response, cancellationToken));
    }

    public async Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        var form = new Dictionary<string, string>
        {
            ["Body"] = string.Empty
        };
        using var content = new FormUrlEncodedContent(form);
        using var response = await HttpClient.PostAsync(MessageInstancePath(providerSid), content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAsync(
        string fromNumber,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SmsMessageSnapshot>();
        var fromIso = from.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var toIso = to.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
        var path = MessagesCollectionPath()
            + "?From=" + Uri.EscapeDataString(fromNumber)
            + "&DateSent%3E=" + Uri.EscapeDataString(fromIso)
            + "&DateSent%3C=" + Uri.EscapeDataString(toIso)
            + "&PageSize=1000";

        while (!string.IsNullOrWhiteSpace(path))
        {
            using var response = await HttpClient.GetAsync(path, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var page = JsonSerializer.Deserialize<ListMessageResponse>(json, JsonOptions)
                ?? new ListMessageResponse();

            if (page.Messages is not null)
            {
                foreach (var message in page.Messages)
                {
                    results.Add(ToSnapshot(message));
                }
            }

            path = ToRelativeMessagingPath(page.NextPageUri);
        }

        return results;
    }

    private string MessagesCollectionPath()
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(Options.AccountSid)}/Messages.json";

    private string MessageInstancePath(string sid)
        => $"2010-04-01/Accounts/{Uri.EscapeDataString(Options.AccountSid)}/Messages/{Uri.EscapeDataString(sid)}.json";

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

        return nextPageUri.TrimStart('/');
    }

    private static async Task<TwilioMessageResource> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TwilioMessageResource>(json, JsonOptions)
            ?? throw new TwilioApiException(response.StatusCode, null, "Message resource was empty.");
    }

    private static SmsMessageSnapshot ToSnapshot(TwilioMessageResource resource)
    {
        return new SmsMessageSnapshot(
            resource.Sid ?? string.Empty,
            resource.From,
            resource.To,
            resource.Body,
            resource.Status,
            resource.ErrorCode,
            resource.ErrorMessage,
            ParseRfc2822(resource.DateCreated),
            ParseRfc2822(resource.DateSent));
    }
}
