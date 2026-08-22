using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsProvider : ISmsProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(
        TwilioSdkClient client,
        IOptions<TwilioSettings> options,
        ILogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: rawNumber,
                    fields: "validation",
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

            if (response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber))
                return new PhoneNumberLookupResult(true, response.PhoneNumber, null);

            if (response.Valid == false)
                return new PhoneNumberLookupResult(false, null, "The number is not a usable destination.");

            throw new SmsProviderUnavailableException("The provider returned a response that could not be processed.");
        }
        catch (SmsProviderUnavailableException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Phone number lookup failed with HTTP {StatusCode}", status);
            if (status is 401 or 403)
                throw new SmsProviderUnavailableException("The messaging provider is unavailable.", status, ex);
            if (status >= 400 && status < 500)
                return new PhoneNumberLookupResult(false, null, "The number is not a usable destination.");
            throw new SmsProviderUnavailableException("The messaging provider is unavailable.", status, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Phone number lookup returned an unreadable response");
            throw new SmsProviderUnavailableException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            _logger.LogWarning("Phone number lookup transport failure");
            throw new SmsProviderUnavailableException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public Task<SmsDispatchResult> SendImmediateAsync(string toCanonical, string body, CancellationToken cancellationToken)
        => CreateMessageAsync(
            toCanonical,
            body,
            scheduleType: null,
            sendAt: null,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            cancellationToken);

    public Task<SmsDispatchResult> ScheduleAsync(string toCanonical, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken)
        => CreateMessageAsync(
            toCanonical,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAtUtc.ToUniversalTime(),
            from: _settings.FromNumber,
            messagingServiceSid: _settings.MessagingServiceSid,
            cancellationToken);

    public async Task<SmsDispatchResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceGate.Begin())
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

                return ToDispatchResult(updated);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw Translate("cancel", ex);
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
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

            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Fetch message failed with HTTP {StatusCode}", status);
            if (status is 401 or 403)
                throw new SmsProviderUnavailableException("The messaging provider is unavailable.", status, ex);
            if (status >= 400 && status < 500)
                return null;
            throw new SmsProviderUnavailableException("The messaging provider is unavailable.", status, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Fetch message returned an unreadable response");
            throw new SmsProviderUnavailableException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw;
            _logger.LogWarning("Fetch message transport failure");
            throw new SmsProviderUnavailableException("The messaging provider is unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceGate.Begin())
            {
                var updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: "",
                        status: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return ToSnapshot(updated);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw Translate("redact", ex);
        }
    }

    public async Task<SmsListPage> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        string? pageToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: toUtc.ToUniversalTime(),
                    dateSentQueryQuery: fromUtc.ToUniversalTime(),
                    pageSize: 1000L,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            var messages = new List<SmsMessageSnapshot>();
            if (response.Messages is not null)
            {
                foreach (var item in response.Messages)
                {
                    var snapshot = ToSnapshot(item);
                    if (snapshot is not null)
                        messages.Add(snapshot);
                }
            }

            var nextToken = ExtractPageToken(response.NextPageUri);
            var hasMore = !string.IsNullOrWhiteSpace(response.NextPageUri) && !string.IsNullOrWhiteSpace(nextToken);
            return new SmsListPage(messages, hasMore ? nextToken : null, hasMore);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw Translate("list", ex);
        }
    }

    private async Task<SmsDispatchResult> CreateMessageAsync(
        string toCanonical,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? from,
        string? messagingServiceSid,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceGate.Begin())
            {
                var created = await Bounded(
                    ct => _client.Api20100401Message.CreateMessage(
                        accountSid: _settings.AccountSid,
                        to: toCanonical,
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

                return ToDispatchResult(created);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw Translate("send", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsProviderUnavailableException Translate(string operation, Exception ex)
    {
        if (ex is SmsProviderUnavailableException already)
            return already;

        if (ex is SdkException<RawError> sdk)
        {
            var status = (int)sdk.Error.StatusCode;
            _logger.LogWarning("Twilio {Operation} failed with HTTP {StatusCode}", operation, status);
            return new SmsProviderUnavailableException("The messaging provider rejected the request.", status, sdk);
        }

        if (ex is JsonException)
        {
            _logger.LogWarning("Twilio {Operation} returned an unreadable response", operation);
            return new SmsProviderUnavailableException("The provider returned a response that could not be processed.", innerException: ex);
        }

        if (ex is TwilioWriteRetryRefusedException)
        {
            _logger.LogWarning("Twilio {Operation} write retry was refused", operation);
            return new SmsProviderUnavailableException("The messaging write outcome is unknown.", innerException: ex);
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Twilio {Operation} transport failure", operation);
            return new SmsProviderUnavailableException("The messaging provider is unreachable.", innerException: ex);
        }

        _logger.LogWarning("Twilio {Operation} failed", operation);
        return new SmsProviderUnavailableException("The messaging provider is unavailable.", innerException: ex);
    }

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message)
    {
        return new SmsDispatchResult(
            ReachedProvider: true,
            ProviderSid: message.Sid,
            Status: message.Status?.Value,
            ErrorCode: message.ErrorCode,
            ErrorMessage: message.ErrorMessage);
    }

    private static SmsMessageSnapshot? ToSnapshot(ApiV2010AccountMessage? message)
    {
        if (message is null || string.IsNullOrWhiteSpace(message.Sid))
            return null;

        return new SmsMessageSnapshot(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.To,
            message.From,
            message.DateSent,
            message.DateCreated);
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
            return null;

        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex >= nextPageUri.Length - 1)
            return null;

        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
                continue;

            var name = Uri.UnescapeDataString(part[..separator]);
            if (!string.Equals(name, "PageToken", StringComparison.OrdinalIgnoreCase))
                continue;

            return Uri.UnescapeDataString(part[(separator + 1)..]);
        }

        return null;
    }
}
