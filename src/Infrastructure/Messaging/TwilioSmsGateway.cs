using System;
using System.Collections.Generic;
using System.Net.Http;
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

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private const int ListPageSize = 1000;
    private const int MaxListPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _settings;
    private readonly TimeSpan _callBudget;
    private readonly TimeSpan _listBudget;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioOptions> options)
    {
        _client = client;
        _settings = options.Value;
        _callBudget = TimeSpan.FromSeconds(20);
        _listBudget = TimeSpan.FromSeconds(60);
    }

    public Task<SmsSendResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken = default) =>
        CreateAsync(
            to: to,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            scheduleType: null,
            sendAt: null,
            cancellationToken: cancellationToken);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default) =>
        CreateAsync(
            to: to,
            body: body,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken: cancellationToken);

    public async Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw Map(ex);
        }
    }

    public async Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return MapMessage(message);
        }
        catch (Exception ex)
        {
            throw Map(ex);
        }
    }

    public async Task RedactBodyAsync(string providerSid, CancellationToken cancellationToken = default)
    {
        try
        {
            await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (Exception ex)
        {
            throw Map(ex);
        }
    }

    public async Task<SmsListResult> ListFromConfiguredSenderAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var collected = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;
        string? previousToken = null;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_listBudget);
            var deadline = cts.Token;

            do
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        collected.Add(MapMessage(message));
                    }
                }

                pages++;
                if (pages >= MaxListPages)
                {
                    truncated = !string.IsNullOrWhiteSpace(page.NextPageUri);
                    break;
                }

                pageToken = ExtractPageToken(page.NextPageUri);
                if (!string.IsNullOrEmpty(pageToken) &&
                    string.Equals(pageToken, previousToken, StringComparison.Ordinal))
                {
                    break;
                }

                previousToken = pageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));
        }
        catch (Exception ex)
        {
            throw Map(ex);
        }

        return new SmsListResult
        {
            Messages = collected,
            Truncated = truncated
        };
    }

    private async Task<SmsSendResult> CreateAsync(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioCreateOnceHandler.BeginCreateScope())
            {
                var created = await Bounded(
                    ct => _client.Api20100401Message.CreateMessage(
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
                        ct: ct),
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(created.Sid))
                {
                    throw new SmsGatewayException("The provider returned a response that could not be processed.");
                }

                return new SmsSendResult
                {
                    Sid = created.Sid,
                    Status = created.Status?.Value,
                    ErrorCode = created.ErrorCode,
                    ErrorMessage = created.ErrorMessage
                };
            }
        }
        catch (Exception ex)
        {
            throw Map(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);
        return await call(cts.Token);
    }

    private static SmsMessageSnapshot MapMessage(ApiV2010AccountMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status?.Value,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        From = message.From,
        To = message.To,
        Body = message.Body,
        DateSent = message.DateSent,
        DateCreated = message.DateCreated
    };

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var relative = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri(new Uri("https://api.twilio.com"), nextPageUri);

        var query = relative.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(part[..separator]);
            if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(separator + 1)..]);
            }
        }

        return null;
    }

    private static SmsGatewayException Map(Exception ex) => ex switch
    {
        SmsGatewayException already => already,
        DuplicateProviderWriteException dup => new SmsGatewayException("The provider write outcome is unknown.", statusCode: null, dup),
        SdkException<RawError> sdk when (int)sdk.Error.StatusCode is 401 or 403 =>
            new SmsGatewayException("The messaging provider is unavailable.", (int)sdk.Error.StatusCode, sdk),
        SdkException<RawError> sdk when (int)sdk.Error.StatusCode == 429 =>
            new SmsGatewayException("The messaging provider is temporarily unavailable.", 429, sdk),
        SdkException<RawError> sdk when (int)sdk.Error.StatusCode >= 400 && (int)sdk.Error.StatusCode < 500 =>
            new SmsGatewayException("The provider rejected the request.", (int)sdk.Error.StatusCode, sdk),
        SdkException<RawError> sdk =>
            new SmsGatewayException("The messaging provider is unavailable.", (int)sdk.Error.StatusCode, sdk),
        System.Text.Json.JsonException json =>
            new SmsGatewayException("The provider returned a response that could not be processed.", statusCode: null, json),
        HttpRequestException http =>
            new SmsGatewayException("The messaging provider is unreachable.", statusCode: null, http),
        TaskCanceledException timeout =>
            new SmsGatewayException("The messaging provider timed out.", statusCode: null, timeout),
        _ => new SmsGatewayException("The messaging provider is unavailable.", statusCode: null, ex)
    };
}
