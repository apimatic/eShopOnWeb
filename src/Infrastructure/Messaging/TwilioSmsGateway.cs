using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsGateway : ISmsNotificationGateway
{
    private const int MaxPages = 50;
    private const long PageSize = 1000L;
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(25);

    public TwilioSmsGateway(TwilioSdk.TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<SmsDispatchResult> SendImmediateAsync(string toE164, string body, CancellationToken cancellationToken)
        => CreateAsync(toE164, body, scheduleType: null, sendAt: null, cancellationToken);

    public Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateAsync(toE164, body, MessageEnumScheduleType.Fixed, sendAt, cancellationToken);

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(message);
        }
        catch (MessagingProviderException)
        {
            throw;
        }
    }

    public async Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        using var write = OnceOnlyWriteHandler.BeginWrite();
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(message);
        }
        catch (DuplicateProviderWriteException ex)
        {
            throw new MessagingProviderException("The cancel request may already have reached the provider.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        using var write = OnceOnlyWriteHandler.BeginWrite();
        try
        {
            await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    ct: ct),
                cancellationToken);

            var fetched = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(fetched);
        }
        catch (DuplicateProviderWriteException ex)
        {
            throw new MessagingProviderException("The disposal request may already have reached the provider.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var messages = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;

        try
        {
            while (true)
            {
                if (++pages > MaxPages)
                {
                    truncated = true;
                    break;
                }

                var capturedToken = pageToken;
                var page = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: PageSize,
                        page: null,
                        pageToken: capturedToken,
                        ct: ct),
                    cancellationToken);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        messages.Add(ToSnapshot(message));
                    }
                }

                if (string.IsNullOrEmpty(page.NextPageUri))
                {
                    break;
                }

                pageToken = ReadPageToken(page.NextPageUri);
                if (string.IsNullOrEmpty(pageToken))
                {
                    break;
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }

        return new SmsMessageListResult(messages, truncated);
    }

    private async Task<SmsDispatchResult> CreateAsync(
        string toE164,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        using var write = OnceOnlyWriteHandler.BeginWrite();
        try
        {
            var scheduled = scheduleType is not null;
            var message = await Bounded(
                ct => _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toE164,
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
                    from: _settings.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: ct),
                cancellationToken);

            return new SmsDispatchResult(
                true,
                message.Sid,
                message.Status?.Value,
                message.ErrorCode,
                message.ErrorMessage);
        }
        catch (DuplicateProviderWriteException ex)
        {
            throw new MessagingProviderException("The send request may already have reached the provider.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    private static SmsMessageSnapshot ToSnapshot(ApiV2010AccountMessage message)
        => new(
            message.Sid,
            message.Status?.Value,
            message.Body,
            message.From,
            message.DateSent,
            message.ErrorCode,
            message.ErrorMessage);

    private static string? ReadPageToken(string nextPageUri)
    {
        Uri uri;
        try
        {
            uri = new Uri(nextPageUri, UriKind.RelativeOrAbsolute);
        }
        catch (UriFormatException)
        {
            return null;
        }

        var query = uri.IsAbsoluteUri
            ? uri.Query
            : new Uri("http://local" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri)).Query;

        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..separator]);
            if (string.Equals(name, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(separator + 1)..]);
            }
        }

        return null;
    }

    private static MessagingProviderException Translate(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        return status switch
        {
            401 or 403 => new MessagingProviderException("The messaging provider is unavailable.", status, ex),
            429 => new MessagingProviderException("The messaging provider is temporarily unavailable.", status, ex),
            >= 400 and < 500 => new MessagingProviderException("The messaging provider rejected the request.", status, ex),
            _ => new MessagingProviderException("The messaging provider is unavailable.", status, ex)
        };
    }
}
