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

public class TwilioSmsGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence",
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

            return EvaluateLookup(response);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is 401 or 403)
            {
                throw new SmsProviderException("Provider authentication failed.", status, ex);
            }

            if (status is >= 400 and < 500)
            {
                return new PhoneLookupResult(false, null, "The number is not a usable destination.");
            }

            throw new SmsProviderException("The provider could not look up the number.", status, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The provider was unreachable.", innerException: ex);
        }
    }

    public Task<SmsSendResult> SendNowAsync(string to, string body, CancellationToken cancellationToken)
        => CreateAsync(to, body, scheduleType: null, sendAt: null, cancellationToken);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateAsync(to, body, MessageEnumScheduleType.Fixed, sendAt, cancellationToken);

    public async Task<SmsSendResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteScope.Begin())
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

                return MapSend(updated, outcomeUnknown: false);
            }
        }
        catch (DuplicateWriteRefusedException)
        {
            return await RecoverUnknownAsync(providerSid, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            if (ex.Error.StatusCode == HttpStatusCode.NotFound)
            {
                return new SmsSendResult(true, providerSid, "failed", (int)ex.Error.StatusCode, "Message not found.", false);
            }

            var fetched = await TryFetchAsync(providerSid, cancellationToken);
            if (fetched != null)
            {
                return MapSend(fetched, outcomeUnknown: false);
            }

            return new SmsSendResult(true, providerSid, "failed", (int)ex.Error.StatusCode, "Cancel was refused.", false);
        }
        catch (JsonException)
        {
            return await RecoverUnknownAsync(providerSid, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return await RecoverUnknownAsync(providerSid, cancellationToken);
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

            return MapSnapshot(message);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException("The provider could not fetch the message.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The provider was unreachable.", innerException: ex);
        }
    }

    public async Task<bool> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteScope.Begin())
            {
                await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: "",
                        status: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
            }

            return true;
        }
        catch (DuplicateWriteRefusedException ex)
        {
            throw new SmsProviderException("The provider write outcome is unknown.", innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException("The provider could not dispose of the message content.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The provider was unreachable.", innerException: ex);
        }
    }

    public async Task<SmsMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        const int maxPages = 50;
        const long pageSize = 100;
        var messages = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int page = 0;
        var truncated = false;

        try
        {
            while (true)
            {
                var response = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: toExclusive,
                        dateSentQueryQuery: fromInclusive,
                        pageSize: pageSize,
                        page: page,
                        pageToken: pageToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                if (response.Messages != null)
                {
                    foreach (var message in response.Messages)
                    {
                        var snapshot = MapSnapshot(message);
                        if (snapshot != null)
                        {
                            messages.Add(snapshot);
                        }
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }

                page++;
                if (page >= maxPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation listing stopped after {MaxPages} pages", maxPages);
                    break;
                }

                pageToken = ReadQueryParam(response.NextPageUri, "PageToken") ?? pageToken;
            }

            return new SmsMessageListResult(messages, truncated);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException("The provider could not list messages.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The provider was unreachable.", innerException: ex);
        }
    }

    private async Task<SmsSendResult> CreateAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteScope.Begin())
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
                        messagingServiceSid: _settings.MessagingServiceSid,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return MapSend(created, outcomeUnknown: false);
            }
        }
        catch (DuplicateWriteRefusedException)
        {
            _logger.LogWarning("A duplicate SMS write was refused before it reached the provider.");
            return new SmsSendResult(true, null, "unknown", null, "Write outcome is unknown.", true);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is 401 or 403)
            {
                _logger.LogWarning("SMS create failed with provider authentication status {Status}", status);
            }
            else
            {
                _logger.LogWarning("SMS create was rejected with status {Status}", status);
            }

            return new SmsSendResult(true, null, "failed", status, "The provider rejected the message.", false);
        }
        catch (JsonException)
        {
            _logger.LogWarning("SMS create returned a body that could not be processed.");
            return new SmsSendResult(true, null, "unknown", null, "Write outcome is unknown.", true);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("SMS create failed because the provider was unreachable.");
            return new SmsSendResult(true, null, "unknown", null, "Write outcome is unknown.", true);
        }
    }

    private static PhoneLookupResult EvaluateLookup(LookupResponse response)
    {
        if (response.Valid != true)
        {
            return new PhoneLookupResult(false, null, DescribeValidation(response));
        }

        if (response.ValidationErrors is { Count: > 0 })
        {
            return new PhoneLookupResult(false, null, "The number is not a usable destination.");
        }

        var lineType = response.LineTypeIntelligence?.Type;
        if (!string.IsNullOrWhiteSpace(lineType)
            && lineType.Equals("landline", StringComparison.OrdinalIgnoreCase))
        {
            return new PhoneLookupResult(false, null, "The number is not a usable SMS destination.");
        }

        if (string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            return new PhoneLookupResult(false, null, "The provider did not return a canonical number.");
        }

        return new PhoneLookupResult(true, response.PhoneNumber, null);
    }

    private static string DescribeValidation(LookupResponse response)
    {
        if (response.ValidationErrors is { Count: > 0 })
        {
            return "The number is not a usable destination.";
        }

        return "The number is not a usable destination.";
    }

    private async Task<SmsSendResult> RecoverUnknownAsync(string providerSid, CancellationToken cancellationToken)
    {
        var fetched = await TryFetchAsync(providerSid, cancellationToken);
        if (fetched != null)
        {
            return MapSend(fetched, outcomeUnknown: false);
        }

        return new SmsSendResult(true, providerSid, "unknown", null, "Write outcome is unknown.", true);
    }

    private async Task<ApiV2010AccountMessage?> TryFetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            return await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
        }
        catch (Exception ex) when (ex is SdkException<RawError> or JsonException or HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static SmsSendResult MapSend(ApiV2010AccountMessage message, bool outcomeUnknown)
        => new(
            true,
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            outcomeUnknown);

    private static SmsMessageSnapshot? MapSnapshot(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            return null;
        }

        return new SmsMessageSnapshot(
            message.Sid,
            message.From,
            message.To,
            message.Status?.Value,
            message.Body,
            message.DateSent,
            message.ErrorCode,
            message.ErrorMessage);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static string? ReadQueryParam(string uriString, string name)
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

            if (!Uri.UnescapeDataString(pair[0]).Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }

        return null;
    }
}
