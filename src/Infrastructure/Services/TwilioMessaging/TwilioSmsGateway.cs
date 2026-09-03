using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.TwilioMessaging;

public class TwilioSmsGateway : ISmsGateway
{
    private const int ListPageSize = 1000;
    private const int MaxListPages = 50;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken)
    {
        return SendCoreAsync(
            to: to,
            body: body,
            scheduleType: null,
            sendAt: null,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            cancellationToken: cancellationToken);
    }

    public Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        return SendCoreAsync(
            to: to,
            body: body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            cancellationToken: cancellationToken);
    }

    public async Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        return await InvokeWriteAsync(async ct =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct);

            return ToResult(message, accepted: true);
        }, cancellationToken);
    }

    public async Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        return await InvokeReadAsync(async ct =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct);

            return ToResult(message, accepted: true);
        }, cancellationToken);
    }

    public async Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        return await InvokeWriteAsync(async ct =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct);

            return ToResult(message, accepted: true);
        }, cancellationToken);
    }

    public async Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var messages = new List<SmsMessageResult>();
        string? pageToken = null;
        int? page = null;
        var truncated = false;

        for (var pages = 0; pages < MaxListPages; pages++)
        {
            var currentPageToken = pageToken;
            var currentPage = page;
            var envelope = await InvokeReadAsync(async ct =>
            {
                return await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: currentPage,
                    pageToken: currentPageToken,
                    requestOptions: null,
                    ct: ct);
            }, cancellationToken);

            if (envelope is null)
            {
                break;
            }

            if (envelope.Messages is not null)
            {
                foreach (var message in envelope.Messages)
                {
                    messages.Add(ToResult(message, accepted: true));
                }
            }

            if (string.IsNullOrWhiteSpace(envelope.NextPageUri))
            {
                return new SmsMessageListResult { Messages = messages, Truncated = false };
            }

            pageToken = GetQueryParam(envelope.NextPageUri, "PageToken");
            var pageValue = GetQueryParam(envelope.NextPageUri, "Page");
            page = int.TryParse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage)
                ? parsedPage
                : pages + 1;
        }

        truncated = true;
        _logger.LogWarning("Reconciliation list stopped after {MaxPages} pages.", MaxListPages);
        return new SmsMessageListResult { Messages = messages, Truncated = truncated };
    }

    private async Task<SmsMessageResult> SendCoreAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? from,
        string? messagingServiceSid,
        CancellationToken cancellationToken)
    {
        return await InvokeWriteAsync(async ct =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
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
                ct: ct);

            return ToResult(message, accepted: true);
        }, cancellationToken);
    }

    private async Task<SmsMessageResult> InvokeWriteAsync(
        Func<CancellationToken, Task<SmsMessageResult>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            using var writeScope = TwilioWriteOnceHandler.BeginWriteScope();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);
            return await call(cts.Token);
        }
        catch (Exception ex)
        {
            return MapFailure(ex, cancellationToken);
        }
    }

    private async Task<T> InvokeReadAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(CallBudget);
            return await call(cts.Token);
        }
        catch (Exception ex)
        {
            if (typeof(T) == typeof(SmsMessageResult))
            {
                return (T)(object)MapFailure(ex, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning("A Twilio read failed.");
            throw new ApplicationCore.Exceptions.TwilioProviderException(
                "The messaging provider is unavailable.",
                502,
                ex);
        }
    }

    private SmsMessageResult MapFailure(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            throw ex;
        }

        switch (ex)
        {
            case SdkException<RawError> sdk:
                var status = (int)sdk.Error.StatusCode;
                _logger.LogWarning("Twilio messaging call failed with provider status {StatusCode}.", status);
                return new SmsMessageResult
                {
                    ProviderAccepted = false,
                    OutcomeDetail = "The provider rejected the messaging request."
                };
            case JsonException:
                _logger.LogWarning("Twilio messaging returned a response that could not be processed.");
                return new SmsMessageResult
                {
                    ProviderAccepted = false,
                    OutcomeDetail = "The provider returned a response that could not be processed."
                };
            case TwilioDuplicateWriteRefusedException:
                _logger.LogWarning("A duplicate Twilio write was refused; outcome is unknown.");
                return new SmsMessageResult
                {
                    ProviderAccepted = false,
                    OutcomeDetail = "The provider call ended with an unknown outcome."
                };
            case HttpRequestException:
            case TaskCanceledException:
            case OperationCanceledException:
                _logger.LogWarning("Twilio messaging could not reach the provider.");
                return new SmsMessageResult
                {
                    ProviderAccepted = false,
                    OutcomeDetail = "The messaging provider is unavailable."
                };
            default:
                _logger.LogWarning("Twilio messaging failed unexpectedly.");
                return new SmsMessageResult
                {
                    ProviderAccepted = false,
                    OutcomeDetail = "The provider call failed."
                };
        }
    }

    private static SmsMessageResult ToResult(ApiV2010AccountMessage message, bool accepted)
    {
        return new SmsMessageResult
        {
            ProviderAccepted = accepted,
            Sid = message.Sid,
            Status = message.Status?.Value,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            Body = message.Body,
            To = message.To,
            From = message.From,
            DateSent = message.DateSent,
            DateCreated = message.DateCreated
        };
    }

    private static string? GetQueryParam(string uriString, string name)
    {
        var queryIndex = uriString.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == uriString.Length - 1)
        {
            return null;
        }

        var query = uriString[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[0]);
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }

        return null;
    }
}
