using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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

public sealed class TwilioMessagingGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int ListPageSize = 1000;
    private const int MaxListPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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

            var errors = lookup.ValidationErrors?
                .Select(error => error.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToList() ?? new List<string>();

            return new PhoneLookupResult(lookup.Valid == true, lookup.PhoneNumber, errors);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500 && status is not 401 and not 403 and not 429)
            {
                return new PhoneLookupResult(false, null, Array.Empty<string>());
            }

            throw MapProviderException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new SmsProviderException("SMS provider unavailable.", innerException: ex);
        }
    }

    public Task<SmsSendResult> SendImmediateAsync(string to, string body, CancellationToken cancellationToken)
    {
        return CreateMessageAsync(
            to: to,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            scheduleType: null,
            sendAt: null,
            cancellationToken: cancellationToken);
    }

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        return CreateMessageAsync(
            to: to,
            body: body,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken: cancellationToken);
    }

    public async Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteGuard.Begin())
            {
                var message = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct), cancellationToken);

                return ToSendResult(message);
            }
        }
        catch (Exception ex) when (IsSendFailure(ex))
        {
            return FailedSend(ex);
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(ct => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct), cancellationToken);

            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            return null;
        }
        catch (Exception ex) when (ex is SdkException<RawError> or HttpRequestException or TaskCanceledException or JsonException)
        {
            throw MapCaught(ex);
        }
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteGuard.Begin())
            {
                var message = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: "",
                    status: null,
                    requestOptions: null,
                    ct: ct), cancellationToken);

                return ToSnapshot(message);
            }
        }
        catch (Exception ex) when (ex is SdkException<RawError> or HttpRequestException or TaskCanceledException or JsonException or DuplicateWritePreventedException)
        {
            throw MapCaught(ex);
        }
    }

    public async Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var collected = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = null;
        var pages = 0;
        var truncated = false;

        try
        {
            while (pages < MaxListPages)
            {
                var response = await Bounded(ct => _client.Api20100401Message.ListMessage(
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
                    ct: ct), cancellationToken);

                pages++;

                if (response.Messages is not null)
                {
                    collected.AddRange(response.Messages
                        .Where(message => !string.IsNullOrEmpty(message.Sid))
                        .Select(ToSnapshot)!);
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }

                pageToken = TryGetPageToken(response.NextPageUri);
                page = (response.Page ?? pages) + 1;

                if (pages >= MaxListPages)
                {
                    truncated = true;
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is SdkException<RawError> or HttpRequestException or TaskCanceledException or JsonException)
        {
            throw MapCaught(ex);
        }

        return new SmsMessageListResult(collected, truncated);
    }

    private async Task<SmsSendResult> CreateMessageAsync(
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
            using (TwilioWriteGuard.Begin())
            {
                var message = await Bounded(ct => _client.Api20100401Message.CreateMessage(
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

                return ToSendResult(message);
            }
        }
        catch (Exception ex) when (IsSendFailure(ex))
        {
            return FailedSend(ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message)
    {
        return new SmsSendResult(
            Succeeded: !string.IsNullOrEmpty(message.Sid),
            ProviderSid: message.Sid,
            Status: message.Status?.Value,
            ErrorCode: message.ErrorCode,
            ErrorMessage: message.ErrorMessage);
    }

    private static SmsMessageSnapshot? ToSnapshot(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            return null;
        }

        return new SmsMessageSnapshot(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.From,
            message.To,
            message.DateSent);
    }

    private static string? TryGetPageToken(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        var query = queryIndex >= 0 ? nextPageUri[(queryIndex + 1)..] : nextPageUri;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static bool IsSendFailure(Exception ex)
    {
        return ex is SdkException<RawError>
            or HttpRequestException
            or TaskCanceledException
            or JsonException
            or DuplicateWritePreventedException;
    }

    private static SmsSendResult FailedSend(Exception ex)
    {
        var status = (ex as SmsProviderException)?.StatusCode
            ?? (ex as SdkException<RawError>)?.Error.StatusCode switch
            {
                HttpStatusCode code => (int)code,
                _ => null
            };

        return new SmsSendResult(false, null, "send_failed", status, "Provider send failed.");
    }

    private static SmsProviderException MapCaught(Exception ex)
    {
        if (ex is SdkException<RawError> sdk)
        {
            return MapProviderException(sdk);
        }

        return new SmsProviderException("SMS provider unavailable.", innerException: ex);
    }

    private static SmsProviderException MapProviderException(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        var message = status switch
        {
            401 or 403 => "SMS provider unavailable.",
            429 => "SMS provider temporarily unavailable.",
            >= 400 and < 500 => "The SMS provider rejected the request.",
            _ => "SMS provider unavailable."
        };

        return new SmsProviderException(message, status, ex);
    }
}
