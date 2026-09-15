using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingGateway : IMessagingProvider
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingGateway> _logger;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(30);

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> options, ILogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
        FollowUpDelay = TimeSpan.FromDays(Math.Max(1, _settings.FollowUpDelayDays));
        FromNumber = _settings.FromNumber;
    }

    public TimeSpan FollowUpDelay { get; }
    public string FromNumber { get; }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null,
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
                ct: ct), cancellationToken);

            if (response.Valid != true)
            {
                _logger.LogInformation("Lookup rejected a number: valid={Valid} validationErrors={ErrorCount} hasCanonical={HasCanonical}",
                    response.Valid, response.ValidationErrors?.Count ?? 0, !string.IsNullOrWhiteSpace(response.PhoneNumber));
                return new PhoneLookupResult(false, null);
            }

            if (response.ValidationErrors is { Count: > 0 })
            {
                _logger.LogInformation("Lookup rejected a number due to validation errors. count={ErrorCount}", response.ValidationErrors.Count);
                return new PhoneLookupResult(false, null);
            }

            if (string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                _logger.LogInformation("Lookup returned no canonical number.");
                return new PhoneLookupResult(false, null);
            }

            return new PhoneLookupResult(true, response.PhoneNumber);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new MessagingProviderException("The messaging provider is unavailable.", status, ex);
            }

            if ((int)status is >= 400 and < 500)
            {
                _logger.LogInformation("Lookup provider returned HTTP {StatusCode}", (int)status);
                return new PhoneLookupResult(false, null);
            }

            throw new MessagingProviderException("The messaging provider is unavailable.", status, ex);
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

    public Task<ProviderMessage> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateAsync(to, body, from: _settings.FromNumber, messagingServiceSid: null, scheduleType: null, sendAt: null, cancellationToken);

    public Task<ProviderMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateAsync(
            to,
            body,
            from: _settings.FromNumber,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken);

    public async Task<ProviderMessage> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        var current = await FetchAsync(providerSid, cancellationToken);
        if (!IsPendingSend(current.Status))
        {
            return current;
        }

        try
        {
            using (TwilioWriteGuard.BeginScope())
            {
                var updated = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct), cancellationToken);
                return Map(updated);
            }
        }
        catch (TwilioDuplicateWriteException ex)
        {
            throw new MessagingProviderException("The messaging provider write outcome is unknown.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
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

    public async Task<ProviderMessage> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(ct => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct), cancellationToken);
            return Map(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
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

    public async Task<ProviderMessage> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteGuard.BeginScope())
            {
                var updated = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: "",
                    status: null,
                    requestOptions: null,
                    ct: ct), cancellationToken);
                return Map(updated);
            }
        }
        catch (TwilioDuplicateWriteException ex)
        {
            throw new MessagingProviderException("The messaging provider write outcome is unknown.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
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

    public async Task<ProviderMessagePage> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        long? pageSize,
        int? page,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(ct => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: toInclusive.AddMilliseconds(1),
                dateSentQueryQuery: fromInclusive.AddMilliseconds(-1),
                pageSize: pageSize,
                page: page,
                pageToken: pageToken,
                requestOptions: null,
                ct: ct), cancellationToken);

            var items = response.Messages?
                .Select(Map)
                .ToList()
                ?? new List<ProviderMessage>();

            return new ProviderMessagePage(items, response.NextPageUri);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
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

    private async Task<ProviderMessage> CreateAsync(
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
            using (TwilioWriteGuard.BeginScope())
            {
                var created = await Bounded(ct => _client.Api20100401Message.CreateMessage(
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
                    ct: ct), cancellationToken);
                return Map(created);
            }
        }
        catch (TwilioDuplicateWriteException ex)
        {
            throw new MessagingProviderException("The messaging provider write outcome is unknown.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
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
        return await call(cts.Token);
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.To,
            message.From,
            message.DateSent,
            message.DateCreated);

    private static bool IsPendingSend(string? status) =>
        string.Equals(status, MessageEnumStatus.Scheduled.Value, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MessageEnumStatus.Queued.Value, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, MessageEnumStatus.Accepted.Value, StringComparison.OrdinalIgnoreCase);
}
