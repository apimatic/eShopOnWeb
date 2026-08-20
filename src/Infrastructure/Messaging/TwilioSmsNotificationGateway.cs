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
using TwilioSdk.Core.Configuration;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private static readonly HashSet<string> UsableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "mobile",
        "personal",
        "fixedVoip",
        "nonFixedVoip",
        "fixed voip",
        "non-fixed voip",
        "voip"
    };

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

    public async Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: "validation,line_type_intelligence",
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
                ct: token), ct);

            return EvaluateLookup(response);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new SmsProviderException("Twilio authentication failed during number lookup.", status, ex);
            }

            if ((int)status >= 400 && (int)status < 500)
            {
                return new SmsLookupResult(false, null, "The provider does not consider this a usable destination.");
            }

            throw new SmsProviderException("Number lookup failed at the provider.", status, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            or TwilioWriteOnceHandler.DuplicateWriteRefusedException)
        {
            throw new SmsProviderException("Number lookup could not be completed.", null, ex);
        }
    }

    public Task<ProviderMessageResult> SendAsync(string to, string body, CancellationToken ct) =>
        CreateAsync(to, body, scheduleType: null, sendAt: null, messagingServiceSid: null, from: _settings.FromNumber, ct);

    public Task<ProviderMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        CreateAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            messagingServiceSid: _settings.MessagingServiceSid,
            from: _settings.FromNumber,
            ct);

    public async Task<ProviderMessageResult> CancelScheduledAsync(string sid, CancellationToken ct)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: sid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: token), ct);
                return Map(message);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Wrap("Could not cancel the scheduled message.", ex);
        }
    }

    public async Task<ProviderMessageResult> FetchAsync(string sid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(token => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: sid,
                ct: token), ct);
            return Map(message);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Wrap("Could not fetch the message from the provider.", ex);
        }
    }

    public async Task<ProviderMessageResult> RedactBodyAsync(string sid, CancellationToken ct)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: sid,
                    body: "",
                    status: null,
                    ct: token), ct);
                return Map(message);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Wrap("Could not dispose of the message content at the provider.", ex);
        }
    }

    public async Task<ProviderMessageListResult> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken ct)
    {
        var collected = new List<ProviderMessageResult>();
        string? pageToken = null;
        int? page = 0;
        var truncated = false;

        try
        {
            for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
            {
                var currentPage = page;
                var currentToken = pageToken;
                var response = await Bounded(token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: toExclusive,
                    dateSentQueryQuery: fromInclusive,
                    pageSize: ListPageSize,
                    page: currentPage,
                    pageToken: currentToken,
                    ct: token), ct);

                if (response.Messages is { Count: > 0 })
                {
                    foreach (var message in response.Messages)
                    {
                        collected.Add(Map(message));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    return new ProviderMessageListResult(collected, Truncated: false);
                }

                pageToken = TryParsePageToken(response.NextPageUri);
                page = (response.Page ?? currentPage ?? 0) + 1;
            }

            truncated = true;
            _logger.LogWarning("Reconciliation list hit the page cap of {MaxPages}; remaining provider pages were not fetched.", MaxListPages);
            return new ProviderMessageListResult(collected, truncated);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Wrap("Could not list messages for reconciliation.", ex);
        }
    }

    private async Task<ProviderMessageResult> CreateAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        string? from,
        CancellationToken ct)
    {
        try
        {
            using (TwilioWriteOnceHandler.BeginWrite())
            {
                var message = await Bounded(token => _client.Api20100401Message.CreateMessage(
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
                    ct: token), ct);
                return Map(message);
            }
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            throw Wrap("Could not send the message.", ex);
        }
    }

    private static SmsLookupResult EvaluateLookup(LookupResponse response)
    {
        if (response.Valid == false)
        {
            return new SmsLookupResult(false, response.PhoneNumber, "The provider does not consider this a usable destination.");
        }

        var lineType = response.LineTypeIntelligence?.Type;
        if (!string.IsNullOrWhiteSpace(lineType) && !IsUsableLineType(lineType))
        {
            return new SmsLookupResult(false, response.PhoneNumber, "The provider does not consider this a usable destination.");
        }

        if (string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            return new SmsLookupResult(false, null, "The provider did not return a canonical form of the number.");
        }

        return new SmsLookupResult(true, response.PhoneNumber, null);
    }

    private static bool IsUsableLineType(string lineType)
    {
        if (UsableLineTypes.Contains(lineType))
        {
            return true;
        }

        return lineType.Contains("mobile", StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderMessageResult Map(ApiV2010AccountMessage message) =>
        new(
            message.Sid,
            StatusWire(message.Status),
            message.Body,
            message.From,
            message.To,
            message.DateSent,
            message.DateCreated,
            message.ErrorCode,
            message.ErrorMessage);

    private static string StatusWire(MessageEnumStatus? status)
    {
        if (status is null)
        {
            return "unknown";
        }

        return status.Value;
    }

    private static string? TryParsePageToken(string nextPageUri)
    {
        try
        {
            var uri = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(nextPageUri)
                : new Uri("https://api.twilio.com" + nextPageUri);
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && parts[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }
        }
        catch (UriFormatException)
        {
            return null;
        }

        return null;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static bool IsProviderFailure(Exception ex) =>
        ex is SdkException<RawError>
            or HttpRequestException
            or TaskCanceledException
            or JsonException
            or TwilioWriteOnceHandler.DuplicateWriteRefusedException
            or OperationCanceledException;

    private static SmsProviderException Wrap(string message, Exception ex)
    {
        HttpStatusCode? status = ex is SdkException<RawError> sdk ? sdk.Error.StatusCode : null;
        return new SmsProviderException(message, status, ex);
    }
}
