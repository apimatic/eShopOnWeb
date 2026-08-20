using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ListBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 20;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsNotificationGateway> _logger;

    public TwilioSmsNotificationGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsNotificationGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        TwilioCallContext.LastStatusCode = null;
        try
        {
            var lookup = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: Field.LineTypeIntelligence.Value,
                    countryCode: null,
                    firstName: null,
                    lastName: null,
                    addressLine1: null,
                    addressLine2: null,
                    city: null,
                    state: null,
                    postalCode: null,
                    addressCountryCode: null,
                    nationalId: null,
                    dateOfBirth: null,
                    lastVerifiedDate: null,
                    verificationSid: null,
                    partnerSubId: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            if (lookup.Valid != true)
            {
                return new PhoneNumberLookupResult(false, null, "The number is not a usable destination.");
            }

            if (lookup.ValidationErrors is { Count: > 0 })
            {
                return new PhoneNumberLookupResult(false, null, "The number is not a usable destination.");
            }

            if (string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new PhoneNumberLookupResult(false, null, "The provider did not return a canonical number.");
            }

            return new PhoneNumberLookupResult(true, lookup.PhoneNumber, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapLookupFailure(ex.Error.StatusCode);
        }
        catch (JsonException)
        {
            throw MapLookupJsonException();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("Provider unavailable.", HttpStatusCode.BadGateway, ex);
        }
    }

    public Task<ProviderMessageResult?> SendImmediateAsync(string to, string body, CancellationToken cancellationToken)
        => CreateSafeAsync(to, body, scheduleType: null, sendAt: null, includeMessagingService: false, cancellationToken);

    public Task<ProviderMessageResult?> ScheduleAsync(
        string to,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
        => CreateSafeAsync(to, body, MessageEnumScheduleType.Fixed, sendAt, includeMessagingService: true, cancellationToken);

    public async Task<ProviderMessageResult?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        TwilioCallContext.LastStatusCode = null;
        try
        {
            using (TwilioWriteOnceScope.Begin())
            {
                var updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return MapMessage(updated);
            }
        }
        catch (Exception ex)
        {
            LogSendPathFailure("cancel", ex);
            return null;
        }
    }

    public async Task<ProviderMessageResult?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        TwilioCallContext.LastStatusCode = null;
        try
        {
            var fetched = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return MapMessage(fetched);
        }
        catch (Exception ex)
        {
            LogSendPathFailure("fetch", ex);
            return null;
        }
    }

    public async Task<ProviderMessageResult?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        TwilioCallContext.LastStatusCode = null;
        try
        {
            using (TwilioWriteOnceScope.Begin())
            {
                var updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: string.Empty,
                        status: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return MapMessage(updated);
            }
        }
        catch (Exception ex)
        {
            LogSendPathFailure("redact", ex);
            return null;
        }
    }

    public async Task<IReadOnlyList<ProviderMessageResult>> ListFromSenderAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<ProviderMessageResult>();
        TwilioCallContext.LastStatusCode = null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(ListBudget);
            var deadline = cts.Token;

            int page = 0;
            string? pageToken = null;
            for (var pages = 0; pages < MaxListPages; pages++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (response.Messages is { Count: > 0 })
                {
                    foreach (var message in response.Messages)
                    {
                        results.Add(MapMessage(message));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri) || response.Messages is not { Count: > 0 })
                {
                    break;
                }

                page++;
            }
        }
        catch (Exception ex)
        {
            LogSendPathFailure("list", ex);
        }

        return results;
    }

    private async Task<ProviderMessageResult?> CreateSafeAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        bool includeMessagingService,
        CancellationToken cancellationToken)
    {
        TwilioCallContext.LastStatusCode = null;
        try
        {
            using (TwilioWriteOnceScope.Begin())
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
                        from: _settings.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: includeMessagingService ? _settings.MessagingServiceSid : null,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return MapMessage(created);
            }
        }
        catch (Exception ex)
        {
            LogSendPathFailure(scheduleType is null ? "send" : "schedule", ex);
            return null;
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessageResult MapMessage(ApiV2010AccountMessage message)
        => new(
            message.Sid,
            message.Status?.Value,
            message.Body,
            message.ErrorCode,
            message.ErrorMessage,
            message.From,
            message.To,
            message.DateCreated,
            message.DateSent);

    private void LogSendPathFailure(string operation, Exception ex)
    {
        if (ex is TwilioDuplicateWriteException)
        {
            _logger.LogWarning("Twilio {Operation} was blocked after an unknown write outcome.", operation);
            return;
        }

        if (ex is SdkException<RawError> sdk)
        {
            _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode}.", operation, (int)sdk.Error.StatusCode);
            return;
        }

        if (ex is JsonException)
        {
            _logger.LogWarning(
                "Twilio {Operation} returned an unreadable body. Last HTTP status {StatusCode}.",
                operation,
                TwilioCallContext.LastStatusCode is { } status ? (int)status : 0);
            return;
        }

        _logger.LogWarning(ex, "Twilio {Operation} failed.", operation);
    }

    private static Exception MapLookupFailure(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        if (code is 401 or 403)
        {
            return new MessagingProviderException("Provider unavailable.", HttpStatusCode.BadGateway);
        }

        if (code is >= 400 and < 500)
        {
            return new UnusableContactNumberException("The number is not a usable destination.");
        }

        return new MessagingProviderException("Provider unavailable.", HttpStatusCode.BadGateway);
    }

    private static Exception MapLookupJsonException()
    {
        var last = TwilioCallContext.LastStatusCode;
        if (last is { } status && (int)status is >= 400 and < 500 and not 401 and not 403)
        {
            return new UnusableContactNumberException("The number is not a usable destination.");
        }

        return new MessagingProviderException(
            "The provider returned a response that could not be processed.",
            HttpStatusCode.BadGateway);
    }
}
