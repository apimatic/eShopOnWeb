using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private const int MaxListPages = 50;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ListBudget = TimeSpan.FromSeconds(45);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> options)
    {
        _client = client;
        _settings = options.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken cancellationToken)
    {
        return WriteAsync(ct => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
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
                scheduleType: null,
                sendAt: null,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct),
            cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        return WriteAsync(ct => _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
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
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: null,
                fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct),
            cancellationToken);
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        var result = await WriteAsync(ct => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct),
            cancellationToken);

        if (result.ProviderSid is not null || result.OutcomeUnknown)
        {
            return result;
        }

        return await FetchAsync(providerSid, cancellationToken);
    }

    public Task<SmsSendResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        return GuardedAsync(ct => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct),
            cancellationToken,
            CallBudget);
    }

    public Task<SmsSendResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        return WriteAsync(ct => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        CancellationToken cancellationToken)
    {
        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        int? page = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(ListBudget);
        var deadline = cts.Token;

        for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
        {
            ListMessageResponse envelope;
            try
            {
                envelope = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: rangeEnd,
                    dateSentQueryQuery: rangeStart,
                    pageSize: 1000,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);
            }
            catch (SdkException<RawError>)
            {
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                break;
            }

            if (envelope.Messages is not null)
            {
                foreach (var message in envelope.Messages)
                {
                    if (!string.IsNullOrEmpty(message.Sid))
                    {
                        results.Add(ToRecord(message));
                    }
                }
            }

            if (string.IsNullOrEmpty(envelope.NextPageUri))
            {
                break;
            }

            pageToken = ExtractPageToken(envelope.NextPageUri);
            page = (envelope.Page ?? pageCount) + 1;
        }

        return results;
    }

    private async Task<SmsSendResult> WriteAsync(Func<CancellationToken, Task<ApiV2010AccountMessage>> call, CancellationToken cancellationToken)
    {
        using (TwilioWriteOnceScope.Begin())
        {
            return await GuardedAsync(call, cancellationToken, CallBudget);
        }
    }

    private static async Task<SmsSendResult> GuardedAsync(
        Func<CancellationToken, Task<ApiV2010AccountMessage>> call,
        CancellationToken cancellationToken,
        TimeSpan budget)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(budget);
            var message = await call(cts.Token);
            return ToResult(message);
        }
        catch (TwilioDuplicateWriteException)
        {
            return SmsSendResult.Unknown("Send outcome is unknown.");
        }
        catch (SdkException<RawError> ex)
        {
            var code = (int)ex.Error.StatusCode;
            return SmsSendResult.Failed("failed", code, "The messaging provider rejected the request.");
        }
        catch (JsonException)
        {
            return SmsSendResult.Unknown("The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return SmsSendResult.Unknown("The messaging provider was unreachable.");
        }
    }

    private static SmsSendResult ToResult(ApiV2010AccountMessage message)
    {
        return new SmsSendResult(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateSent,
            OutcomeUnknown: false);
    }

    private static ProviderMessageRecord ToRecord(ApiV2010AccountMessage message)
    {
        DateTimeOffset? parsedSent = null;
        if (!string.IsNullOrEmpty(message.DateSent) && DateTimeOffset.TryParse(message.DateSent, out var dto))
        {
            parsedSent = dto;
        }

        return new ProviderMessageRecord(
            message.Sid ?? string.Empty,
            message.Status?.Value,
            parsedSent,
            message.DateSent,
            message.From,
            message.To,
            message.Body,
            message.ErrorCode,
            message.ErrorMessage,
            message.Direction?.Value);
    }

    private static string? ExtractPageToken(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return null;
        }

        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..eq]);
            if (name.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }
}
