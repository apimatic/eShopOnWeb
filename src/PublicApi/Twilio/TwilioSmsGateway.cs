using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.PublicApi.Twilio;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

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

    public string SendingNumber => _settings.FromNumber;

    public async Task<SmsLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
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
                    ct: ct),
                cancellationToken);

            if (lookup.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new SmsLookupResult(true, lookup.PhoneNumber, null);
            }

            return new SmsLookupResult(false, null, "The phone number is not a usable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Phone number lookup failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500 && status is not 401 and not 403 and not 429)
            {
                return new SmsLookupResult(false, null, "The phone number is not a usable destination.");
            }

            return new SmsLookupResult(false, null, "The phone number could not be validated.");
        }
        catch (JsonException)
        {
            _logger.LogWarning("Phone number lookup returned a response that could not be processed.");
            return new SmsLookupResult(false, null, "The phone number could not be validated.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Phone number lookup could not reach the provider.");
            return new SmsLookupResult(false, null, "The phone number could not be validated.");
        }
    }

    public Task<SmsMessageSnapshot> SendImmediateAsync(string to, string body, CancellationToken cancellationToken)
        => CreateAsync(to, body, from: _settings.FromNumber, messagingServiceSid: null, scheduleType: null, sendAt: null, cancellationToken);

    public Task<SmsMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateAsync(
            to,
            body,
            from: _settings.FromNumber,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken);

    public async Task<SmsMessageSnapshot> FetchAsync(string sid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: sid,
                    ct: ct),
                cancellationToken);

            return Map(message, succeeded: true);
        }
        catch (Exception ex)
        {
            return MapFailure(ex, "fetch");
        }
    }

    public Task<SmsMessageSnapshot> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
        => UpdateAsync(sid, body: null, status: MessageEnumUpdateStatus.Canceled, cancellationToken);

    public Task<SmsMessageSnapshot> RedactBodyAsync(string sid, CancellationToken cancellationToken)
        => UpdateAsync(sid, body: string.Empty, status: null, cancellationToken);

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromAppAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = 0;
        const long pageSize = 1000;
        const int maxPages = 50;

        try
        {
            for (var pages = 0; pages < maxPages; pages++)
            {
                var currentToken = pageToken;
                var currentPage = page;
                var response = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: pageSize,
                        page: currentPage,
                        pageToken: currentToken,
                        ct: ct),
                    cancellationToken);

                if (response.Messages is { Count: > 0 })
                {
                    foreach (var message in response.Messages)
                    {
                        results.Add(Map(message, succeeded: true));
                    }
                }

                if (string.IsNullOrWhiteSpace(response.NextPageUri))
                {
                    break;
                }

                pageToken = GetQueryParam(response.NextPageUri, "PageToken");
                var nextPage = GetQueryParam(response.NextPageUri, "Page");
                page = int.TryParse(nextPage, out var parsedPage) ? parsedPage : (currentPage ?? 0) + 1;

                if (response.Messages is null || response.Messages.Count == 0)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Message reconciliation listing failed: {Reason}", SafeReason(ex));
        }

        return results;
    }

    private async Task<SmsMessageSnapshot> CreateAsync(
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
            using (TwilioOnceWriteHandler.BeginWrite())
            {
                var message = await Bounded(
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
                        ct: ct),
                    cancellationToken);

                return Map(message, succeeded: true);
            }
        }
        catch (Exception ex)
        {
            return MapFailure(ex, "create");
        }
    }

    private async Task<SmsMessageSnapshot> UpdateAsync(
        string sid,
        string? body,
        MessageEnumUpdateStatus? status,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioOnceWriteHandler.BeginWrite())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: sid,
                        body: body,
                        status: status,
                        ct: ct),
                    cancellationToken);

                return Map(message, succeeded: true);
            }
        }
        catch (Exception ex)
        {
            return MapFailure(ex, "update");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsMessageSnapshot MapFailure(Exception ex, string operation)
    {
        if (ex is TwilioDuplicateWriteException)
        {
            _logger.LogWarning("Blocked a duplicate messaging {Operation} after a transport retry.", operation);
            return new SmsMessageSnapshot(false, null, "failed", null, "Duplicate provider write was blocked.", null, null, null, null, null);
        }

        if (ex is SdkException<RawError> sdk)
        {
            _logger.LogWarning("Messaging {Operation} failed with HTTP {StatusCode}.", operation, (int)sdk.Error.StatusCode);
            return new SmsMessageSnapshot(false, null, "failed", (int)sdk.Error.StatusCode, "The messaging provider rejected the request.", null, null, null, null, null);
        }

        if (ex is JsonException)
        {
            _logger.LogWarning("Messaging {Operation} returned a response that could not be processed.", operation);
            return new SmsMessageSnapshot(false, null, "failed", null, "The provider returned a response that could not be processed.", null, null, null, null, null);
        }

        if (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Messaging {Operation} could not reach the provider.", operation);
            return new SmsMessageSnapshot(false, null, "failed", null, "The messaging provider could not be reached.", null, null, null, null, null);
        }

        _logger.LogWarning("Messaging {Operation} failed unexpectedly.", operation);
        return new SmsMessageSnapshot(false, null, "failed", null, "The messaging provider could not be reached.", null, null, null, null, null);
    }

    private static SmsMessageSnapshot Map(ApiV2010AccountMessage message, bool succeeded)
        => new(
            succeeded,
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.To,
            message.From,
            message.DateSent,
            message.DateCreated);

    private static string SafeReason(Exception ex) => ex switch
    {
        SdkException<RawError> sdk => $"HTTP {(int)sdk.Error.StatusCode}",
        JsonException => "unreadable response",
        HttpRequestException => "transport failure",
        TaskCanceledException => "timeout",
        _ => "unexpected error"
    };

    private static string? GetQueryParam(string uri, string name)
    {
        var queryIndex = uri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == uri.Length - 1)
        {
            return null;
        }

        var query = uri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 0)
            {
                continue;
            }

            if (!string.Equals(Uri.UnescapeDataString(pair[0]), name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
        }

        return null;
    }
}
