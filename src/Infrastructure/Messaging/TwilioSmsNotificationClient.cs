using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioSmsNotificationClient : ISmsNotificationClient
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsNotificationClient> _logger;

    public TwilioSmsNotificationClient(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioSmsNotificationClient> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
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

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Cast<string>()
                .ToList() ?? [];

            return new PhoneNumberLookupResult
            {
                IsValid = response.Valid == true,
                CanonicalNumber = response.PhoneNumber,
                ValidationErrors = errors
            };
        }
        catch (Exception ex)
        {
            throw Translate("lookup", ex);
        }
    }

    public Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken) =>
        CreateAsync(to, body, scheduleType: null, sendAt: null, messagingServiceSid: null, from: _settings.FromNumber, cancellationToken);

    public Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt.ToUniversalTime(),
            messagingServiceSid: _settings.MessagingServiceSid,
            from: null,
            cancellationToken);

    public async Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using var write = TwilioOnceOnlyWriteHandler.BeginWrite();
            var response = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw Translate("cancel", ex);
        }
    }

    public async Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw Translate("fetch", ex);
        }
    }

    public async Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using var write = TwilioOnceOnlyWriteHandler.BeginWrite();
            var response = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: "",
                    status: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return Map(response);
        }
        catch (Exception ex)
        {
            throw Translate("redact", ex);
        }
    }

    public async Task<SmsReconciliationPage> ListSentFromAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        const int maxPages = 50;
        var collected = new List<SmsMessageResult>();
        string? pageToken = null;
        var pages = 0;
        var truncated = false;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(60));
        var deadline = cts.Token;

        try
        {
            do
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: toExclusive.ToUniversalTime(),
                    dateSentQueryQuery: fromInclusive.ToUniversalTime(),
                    pageSize: 1000L,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: deadline);

                if (page.Messages is not null)
                {
                    collected.AddRange(page.Messages.Select(Map));
                }

                pageToken = TryGetPageToken(page.NextPageUri);
                pages++;
                if (pageToken is not null && pages >= maxPages)
                {
                    truncated = true;
                    _logger.LogWarning("Reconciliation listing stopped after {PageCount} pages.", pages);
                    break;
                }
            } while (pageToken is not null);
        }
        catch (Exception ex)
        {
            throw Translate("list", ex);
        }

        return new SmsReconciliationPage
        {
            FromNumber = _settings.FromNumber,
            Messages = collected,
            Truncated = truncated
        };
    }

    private async Task<SmsMessageResult> CreateAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        string? from,
        CancellationToken cancellationToken)
    {
        try
        {
            using var write = TwilioOnceOnlyWriteHandler.BeginWrite();
            var response = await Bounded(
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
            return Map(response);
        }
        catch (Exception ex)
        {
            throw Translate("create", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsProviderException Translate(string operation, Exception ex)
    {
        switch (ex)
        {
            case DuplicateWriteRefusedException:
                _logger.LogWarning("A duplicate messaging write was refused for {Operation}.", operation);
                return new SmsProviderException("The messaging provider outcome is unknown.", innerException: ex);

            case SdkException<RawError> sdk:
                _logger.LogWarning(
                    "Messaging {Operation} failed with HTTP {Status}.",
                    operation,
                    (int)sdk.Error.StatusCode);
                return new SmsProviderException("The messaging provider rejected the request.", sdk.Error.StatusCode, sdk);

            case JsonException:
                _logger.LogWarning("Messaging {Operation} returned an unreadable body.", operation);
                return new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);

            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning("Messaging {Operation} did not complete ({Reason}).", operation, ex.GetType().Name);
                return new SmsProviderException("The messaging provider could not be reached.", innerException: ex);

            default:
                _logger.LogWarning("Messaging {Operation} failed ({Reason}).", operation, ex.GetType().Name);
                return new SmsProviderException("The messaging provider could not complete the request.", innerException: ex);
        }
    }

    private static SmsMessageResult Map(ApiV2010AccountMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status?.Value,
        To = message.To,
        From = message.From,
        Body = message.Body,
        DateCreated = message.DateCreated,
        DateSent = message.DateSent,
        DateUpdated = message.DateUpdated,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        MessagingServiceSid = message.MessagingServiceSid
    };

    private static string? TryGetPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
