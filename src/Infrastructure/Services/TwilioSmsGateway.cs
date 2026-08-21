using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, TwilioSettings settings, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence,line_status",
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
                _logger.LogWarning("Lookup rejected a destination. Valid={Valid}, validationErrorCount={ValidationErrorCount}",
                    lookup.Valid, lookup.ValidationErrors?.Count ?? 0);
                return new PhoneNumberLookupResult { IsUsable = false };
            }

            if (lookup.ValidationErrors is { Count: > 0 })
            {
                _logger.LogWarning("Lookup returned validation errors. Count={ValidationErrorCount}", lookup.ValidationErrors.Count);
                return new PhoneNumberLookupResult { IsUsable = false };
            }

            if (string.IsNullOrEmpty(lookup.PhoneNumber))
            {
                return new PhoneNumberLookupResult { IsUsable = false };
            }

            return new PhoneNumberLookupResult
            {
                IsUsable = true,
                CanonicalNumber = lookup.PhoneNumber
            };
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is >= 400 and < 500 && (int)ex.Error.StatusCode is not 401 and not 403)
        {
            return new PhoneNumberLookupResult { IsUsable = false };
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not validate the destination number.");
        }
    }

    public Task<SmsDispatchResult?> SendAsync(string to, string body, CancellationToken cancellationToken)
        => CreateMessageAsync(to, body, scheduleType: null, sendAt: null, useMessagingService: false, cancellationToken);

    public Task<SmsDispatchResult?> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateMessageAsync(to, body, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, useMessagingService: true, cancellationToken);

    public async Task<SmsDispatchResult?> FetchAsync(string providerSid, CancellationToken cancellationToken)
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

            return ToDispatchResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not read the message from the provider.");
        }
    }

    public async Task<bool> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            ApiV2010AccountMessage updated;
            using (TwilioOnceWriteHandler.BeginWrite())
            {
                updated = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
            }

            return updated.Status == MessageEnumStatus.Canceled;
        }
        catch (SdkException<RawError>)
        {
            try
            {
                var current = await FetchAsync(providerSid, cancellationToken);
                if (current is null)
                {
                    return false;
                }

                if (current.Status is "canceled" or "sent" or "delivered" or "undelivered" or "failed")
                {
                    return current.Status == "canceled";
                }
            }
            catch (SmsProviderException)
            {
                // fall through
            }

            _logger.LogWarning("Could not cancel scheduled message {ProviderSid}", providerSid);
            return false;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not cancel the scheduled message.");
        }
    }

    public async Task<bool> RedactBodyAsync(string providerSid, string originalBody, CancellationToken cancellationToken)
    {
        try
        {
            await TryRedactUpdateAsync(providerSid, cancellationToken);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            try
            {
                await TryRedactUpdateAsync(providerSid, cancellationToken);
            }
            catch (Exception retryEx)
            {
                throw Translate(retryEx, "Could not dispose of the message content at the provider.");
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not dispose of the message content at the provider.");
        }

        try
        {
            var fetched = await FetchAsync(providerSid, cancellationToken);
            if (fetched is null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(originalBody))
            {
                return true;
            }

            return !string.Equals(fetched.Body, originalBody, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not dispose of the message content at the provider.");
        }
    }

    private async Task TryRedactUpdateAsync(string providerSid, CancellationToken cancellationToken)
    {
        using (TwilioOnceWriteHandler.BeginWrite())
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
    }

    public async Task<ProviderMessageList> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        var collected = new List<ProviderMessageRecord>();
        string? pageToken = null;
        int pages = 0;
        var truncated = false;

        try
        {
            while (pages < MaxListPages)
            {
                var pageIndex = pages;
                var token = pageToken;
                var page = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: toExclusive,
                        dateSentQueryQuery: fromInclusive,
                        pageSize: ListPageSize,
                        page: pageIndex,
                        pageToken: token,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                pages++;

                if (page.Messages is { Count: > 0 })
                {
                    foreach (var message in page.Messages)
                    {
                        if (string.IsNullOrEmpty(message.Sid))
                        {
                            continue;
                        }

                        collected.Add(new ProviderMessageRecord
                        {
                            ProviderSid = message.Sid,
                            Status = StatusWire(message.Status),
                            Body = message.Body,
                            DateSent = message.DateSent,
                            DateCreated = message.DateCreated,
                            ErrorCode = message.ErrorCode,
                            ErrorMessage = message.ErrorMessage
                        });
                    }
                }

                if (string.IsNullOrEmpty(page.NextPageUri) || page.Messages is not { Count: > 0 })
                {
                    break;
                }

                pageToken = ExtractPageToken(page.NextPageUri);
                if (pages >= MaxListPages)
                {
                    truncated = true;
                    break;
                }
            }

            if (pages >= MaxListPages && collected.Count > 0)
            {
                truncated = true;
            }

            return new ProviderMessageList { Messages = collected, Truncated = truncated };
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not list messages from the provider.");
        }
    }

    private async Task<SmsDispatchResult?> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        bool useMessagingService,
        CancellationToken cancellationToken)
    {
        try
        {
            ApiV2010AccountMessage created;
            using (TwilioOnceWriteHandler.BeginWrite())
            {
                created = await Bounded(
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
                        from: useMessagingService ? _settings.FromNumber : _settings.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: useMessagingService ? _settings.MessagingServiceSid : null,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
            }

            return ToDispatchResult(created);
        }
        catch (TwilioDuplicateWriteException ex)
        {
            _logger.LogWarning(ex, "A write was blocked after an unknown transport outcome.");
            return null;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "Could not send the message.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsDispatchResult? ToDispatchResult(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            return null;
        }

        return new SmsDispatchResult
        {
            ProviderSid = message.Sid,
            Status = StatusWire(message.Status),
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage,
            Body = message.Body
        };
    }

    private static string StatusWire(MessageEnumStatus? status) => status?.Value ?? "unknown";

    private static string? ExtractPageToken(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?');
        var query = queryIndex >= 0 ? nextPageUri[(queryIndex + 1)..] : nextPageUri;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }

    private SmsProviderException Translate(Exception ex, string callerSafeMessage)
    {
        switch (ex)
        {
            case SmsProviderException already:
                return already;
            case SdkException<RawError> sdk:
                {
                    var status = (int)sdk.Error.StatusCode;
                    _logger.LogWarning("Twilio returned HTTP {StatusCode} for a messaging call.", status);
                    return new SmsProviderException(callerSafeMessage, status, sdk);
                }
            case JsonException json:
                _logger.LogWarning(json, "Twilio returned a response that could not be processed.");
                return new SmsProviderException("The provider returned a response that could not be processed.", innerException: json);
            case TwilioDuplicateWriteException dup:
                return new SmsProviderException("The messaging provider is unreachable.", innerException: dup);
            case Exception transport when transport is HttpRequestException or TaskCanceledException:
                _logger.LogWarning(transport, "Twilio transport failure.");
                return new SmsProviderException("The messaging provider is unreachable.", innerException: transport);
            default:
                _logger.LogWarning(ex, "Unexpected Twilio failure.");
                return new SmsProviderException(callerSafeMessage, innerException: ex);
        }
    }
}
