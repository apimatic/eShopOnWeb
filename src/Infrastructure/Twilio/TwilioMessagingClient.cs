using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessagingClient : IMessageProvider
{
    private readonly HttpClient _client;
    private readonly TwilioOptions _options;
    private readonly string _messagesPath;

    public TwilioMessagingClient(HttpClient client, IOptions<TwilioOptions> options)
    {
        _client = client;
        _options = options.Value;
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? TwilioOptions.DefaultMessagingBaseUrl
            : _options.BaseUrl!;
        TwilioClientSupport.Configure(_client, baseUrl, _options);
        _messagesPath = $"2010-04-01/Accounts/{Uri.EscapeDataString(_options.AccountSid)}/Messages";
    }

    public async Task<ProviderMessage> SendAsync(string destination, string body,
        DateTimeOffset? sendAt = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
            throw new ProviderRequestException("message creation");
        if (sendAt.HasValue && string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            throw new ProviderRequestException("message scheduling");

        // CreateMessage from twilio_api_v2010.yaml (application/x-www-form-urlencoded).
        var fields = new List<KeyValuePair<string, string>>
        {
            new("To", destination),
            new("From", _options.FromNumber),
            new("Body", body)
        };
        if (!string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
            fields.Add(new("MessagingServiceSid", _options.MessagingServiceSid));
        if (sendAt.HasValue)
        {
            fields.Add(new("ScheduleType", "fixed"));
            fields.Add(new("SendAt", sendAt.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
        }

        using var response = await _client.PostAsync($"{_messagesPath}.json",
            new FormUrlEncodedContent(fields), cancellationToken);
        return await ReadMessageAsync(response, sendAt.HasValue ? "message scheduling" : "message creation",
            cancellationToken);
    }

    public Task<ProviderMessage> FetchAsync(string providerMessageSid,
        CancellationToken cancellationToken = default) =>
        GetMessageAsync(providerMessageSid, cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerMessageSid,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(providerMessageSid, new[] { new KeyValuePair<string, string>("Status", "canceled") },
            "message cancellation", cancellationToken);

    public Task<ProviderMessage> RedactContentAsync(string providerMessageSid,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(providerMessageSid, new[] { new KeyValuePair<string, string>("Body", string.Empty) },
            "message content redaction", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from) throw new ArgumentException("The end of the range must not precede its start.");
        if (string.IsNullOrWhiteSpace(_options.FromNumber))
            throw new ProviderRequestException("message reconciliation");

        // ListMessage from twilio_api_v2010.yaml. Critically, From is sent to Twilio on
        // the initial provider query; this is not an application-side sender filter.
        var query = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("From", _options.FromNumber),
            new KeyValuePair<string, string>("DateSent>", from.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("DateSent<", to.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)),
            new KeyValuePair<string, string>("PageSize", "1000")
        });
        var queryString = await query.ReadAsStringAsync(cancellationToken);
        string? next = $"{_messagesPath}.json?{queryString}";
        var seenPages = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<ProviderMessage>();

        while (!string.IsNullOrWhiteSpace(next) && seenPages.Add(next))
        {
            using var response = await _client.GetAsync(ToRelativeProviderUri(next), cancellationToken);
            await TwilioClientSupport.EnsureSuccessAsync(response, "message reconciliation", cancellationToken);
            var page = await response.Content.ReadFromJsonAsync<TwilioMessageListResponse>(
                TwilioClientSupport.JsonOptions, cancellationToken)
                ?? throw new ProviderRequestException("message reconciliation");
            results.AddRange(page.Messages.Select(ToProviderMessage));
            next = page.NextPageUri;
        }

        return results
            .Where(x => x.DateSent is null || (x.DateSent >= from && x.DateSent <= to))
            .ToList();
    }

    private async Task<ProviderMessage> GetMessageAsync(string sid, CancellationToken cancellationToken)
    {
        using var response = await _client.GetAsync(MessagePath(sid), cancellationToken);
        return await ReadMessageAsync(response, "message fetch", cancellationToken);
    }

    private async Task<ProviderMessage> UpdateAsync(string sid,
        IEnumerable<KeyValuePair<string, string>> fields, string operation,
        CancellationToken cancellationToken)
    {
        // UpdateMessage from twilio_api_v2010.yaml: Status=canceled or Body="".
        using var response = await _client.PostAsync(MessagePath(sid),
            new FormUrlEncodedContent(fields), cancellationToken);
        return await ReadMessageAsync(response, operation, cancellationToken);
    }

    private async Task<ProviderMessage> ReadMessageAsync(HttpResponseMessage response, string operation,
        CancellationToken cancellationToken)
    {
        await TwilioClientSupport.EnsureSuccessAsync(response, operation, cancellationToken);
        var message = await response.Content.ReadFromJsonAsync<TwilioMessageResponse>(
            TwilioClientSupport.JsonOptions, cancellationToken)
            ?? throw new ProviderRequestException(operation);
        return ToProviderMessage(message);
    }

    private string MessagePath(string sid) =>
        $"{_messagesPath}/{Uri.EscapeDataString(sid)}.json";

    private static string ToRelativeProviderUri(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var absolute))
            return absolute.PathAndQuery.TrimStart('/');
        return uri.TrimStart('/');
    }

    private static ProviderMessage ToProviderMessage(TwilioMessageResponse message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid) || string.IsNullOrWhiteSpace(message.Status))
            throw new ProviderRequestException("message response parsing");
        return new ProviderMessage(message.Sid, message.Status, message.ErrorCode,
            TwilioClientSupport.ParseDate(message.DateCreated),
            TwilioClientSupport.ParseDate(message.DateSent));
    }
}
