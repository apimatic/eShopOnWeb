using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public string FromNumber => _options.FromNumber;

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        WriteOnce(() => Bounded(
            ct => CreateMessageAsync(
                to: to,
                body: body,
                from: _options.FromNumber,
                messagingServiceSid: null,
                scheduleType: null,
                sendAt: null,
                ct: ct),
            cancellationToken));

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        WriteOnce(() => Bounded(
            ct => CreateMessageAsync(
                to: to,
                body: body,
                from: _options.FromNumber,
                messagingServiceSid: _options.MessagingServiceSid,
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt,
                ct: ct),
            cancellationToken));

    public Task<SmsSendResult> FetchAsync(string sid, CancellationToken cancellationToken) =>
        Bounded(
            ct => Guarded(async () => Map(await _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                requestOptions: null,
                ct: ct))),
            cancellationToken);

    public async Task<SmsSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
    {
        SmsProviderException? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await WriteOnce(() => Bounded(
                    ct => Guarded(async () => Map(await _client.Api20100401Message.UpdateMessage(
                        accountSid: _options.AccountSid,
                        sid: sid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct))),
                    cancellationToken));
            }
            catch (SmsProviderException ex) when ((int?)ex.StatusCode == 404 && attempt < 2)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(400 * (attempt + 1)), cancellationToken);
            }
        }

        throw last ?? new SmsProviderException("The scheduled message could not be cancelled.");
    }

    public Task<SmsSendResult> RedactBodyAsync(string sid, CancellationToken cancellationToken) =>
        WriteOnce(() => Bounded(
            ct => Guarded(async () => Map(await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct))),
            cancellationToken));

    public async Task<IReadOnlyList<SmsListItem>> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var items = new List<SmsListItem>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;
        var hasNext = false;

        do
        {
            var response = await Bounded(
                ct => Guarded(() => _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct)),
                cancellationToken);

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    items.Add(new SmsListItem(
                        message.Sid,
                        StatusOf(message.Status),
                        message.Body,
                        message.From,
                        message.To,
                        message.DateSent,
                        message.DateCreated));
                }
            }

            pages++;
            hasNext = !string.IsNullOrEmpty(response.NextPageUri);
            pageToken = ExtractPageToken(response.NextPageUri);
            if (hasNext && pageToken is null)
            {
                page = (page ?? 0) + 1;
            }
        }
        while (hasNext && pages < MaxListPages);

        return items;
    }

    private async Task<SmsSendResult> CreateMessageAsync(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        return await Guarded(async () => Map(await _client.Api20100401Message.CreateMessage(
            accountSid: _options.AccountSid,
            to: to,
            statusCallback: null,
            applicationSid: null,
            maxPrice: null,
            provideFeedback: null,
            attempt: null,
            validityPeriod: null,
            forceDelivery: null,
            contentRetention: null,
            addressRetention: null,
            smartEncoded: null,
            persistentAction: null,
            trafficType: null,
            shortenUrls: null,
            scheduleType: scheduleType,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: from,
            fallbackFrom: null,
            messagingServiceSid: messagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: ct)));
    }

    private static SmsSendResult Map(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            StatusOf(message.Status),
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.From,
            message.To,
            message.DateSent);

    private static string StatusOf(MessageEnumStatus? status) =>
        status is null ? "unknown" : status.Value;

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                var token = Uri.UnescapeDataString(parts[1]);
                return string.IsNullOrEmpty(token) ? null : token;
            }
        }

        return null;
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static async Task<T> WriteOnce<T>(Func<Task<T>> call)
    {
        SingleAttemptWriteHandler.BeginWrite();
        try
        {
            return await call();
        }
        finally
        {
            SingleAttemptWriteHandler.EndWrite();
        }
    }

    private static async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            var extra = string.Empty;
            try
            {
                var raw = ex.Error.ReadAsString();
                if (!string.IsNullOrEmpty(raw))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(raw, @"""code""\s*:\s*(\d+)");
                    if (match.Success)
                    {
                        extra = $" providerCode={match.Groups[1].Value}";
                    }
                }
            }
            catch
            {
                // ignore unreadable error bodies
            }

            throw new SmsProviderException(
                $"The messaging provider rejected the request (HTTP {(int)ex.Error.StatusCode}{extra}).",
                ex.Error.StatusCode);
        }
        catch (System.Text.Json.JsonException)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.");
        }
        catch (DuplicateProviderWriteException)
        {
            throw new SmsProviderException("The message may already have reached the provider; a duplicate attempt was blocked.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", inner: ex);
        }
    }
}
