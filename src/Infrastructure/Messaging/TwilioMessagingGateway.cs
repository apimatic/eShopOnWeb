using System;
using System.Collections.Generic;
using System.Linq;
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
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 20;
    private const int ListPageSize = 200;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: null,
                    countryCode: countryCode,
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

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrEmpty(v))
                .Cast<string>()
                .ToList() ?? new List<string>();

            return new PhoneLookupResult(
                response.Valid == true,
                response.PhoneNumber,
                errors);
        }
        catch (Exception ex)
        {
            throw Translate("The number could not be verified as a usable destination.", ex);
        }
    }

    public Task<ProviderMessageSnapshot> SendSmsAsync(string to, string body, CancellationToken cancellationToken)
        => CreateMessageAsync(to, body, scheduleType: null, sendAt: null, useMessagingService: false, cancellationToken);

    public Task<ProviderMessageSnapshot> ScheduleSmsAsync(
        string to,
        string body,
        DateTimeOffset sendAt,
        CancellationToken cancellationToken)
        => CreateMessageAsync(
            to,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            useMessagingService: true,
            cancellationToken);

    public async Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (SingleAttemptWriteScope.Begin())
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
        }
        catch (Exception ex)
        {
            throw Translate("The scheduled message could not be cancelled.", ex);
        }
    }

    public async Task<ProviderMessageSnapshot> FetchMessageAsync(string providerSid, CancellationToken cancellationToken)
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
        catch (Exception ex)
        {
            throw Translate("The message could not be retrieved.", ex);
        }
    }

    public async Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (SingleAttemptWriteScope.Begin())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: providerSid,
                        body: "",
                        status: null,
                        ct: ct),
                    cancellationToken);
                return ToSnapshot(message);
            }
        }
        catch (Exception ex)
        {
            throw Translate("Message content could not be disposed of at the provider.", ex);
        }
    }

    public async Task<(IReadOnlyList<ProviderMessageSnapshot> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        try
        {
            var collected = new List<ProviderMessageSnapshot>();
            string? pageToken = null;
            int? page = null;
            var truncated = false;

            for (var pages = 0; pages < MaxListPages; pages++)
            {
                var response = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: toExclusive,
                        dateSentQueryQuery: fromInclusive,
                        pageSize: ListPageSize,
                        page: page,
                        pageToken: pageToken,
                        ct: ct),
                    cancellationToken);

                if (response.Messages is not null)
                {
                    collected.AddRange(response.Messages.Select(ToSnapshot));
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    return (collected, false);
                }

                pageToken = PageTokenFrom(response.NextPageUri);
                page = response.Page is null ? null : response.Page + 1;
            }

            truncated = true;
            _logger.LogWarning("Reconciliation list stopped after {MaxPages} pages.", MaxListPages);
            return (collected, truncated);
        }
        catch (Exception ex)
        {
            throw Translate("The provider message list could not be retrieved.", ex);
        }
    }

    private async Task<ProviderMessageSnapshot> CreateMessageAsync(
        string to,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        bool useMessagingService,
        CancellationToken cancellationToken)
    {
        try
        {
            using (SingleAttemptWriteScope.Begin())
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
                        from: _settings.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: useMessagingService ? _settings.MessagingServiceSid : null,
                        body: body,
                        mediaUrl: null,
                        contentSid: null,
                        ct: ct),
                    cancellationToken);
                return ToSnapshot(message);
            }
        }
        catch (Exception ex)
        {
            throw Translate("The message could not be accepted by the provider.", ex);
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessageSnapshot ToSnapshot(TwilioSdk.Models.ApiV2010AccountMessage message)
        => new(
            message.Sid,
            message.Status?.Value,
            message.Body,
            message.From,
            message.To,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateCreated,
            message.DateSent);

    private MessagingProviderException Translate(string callerSafeMessage, Exception ex)
    {
        if (ex is SdkException<RawError> sdk)
        {
            _logger.LogWarning("Twilio request failed with HTTP {StatusCode}.", (int)sdk.Error.StatusCode);
            return new MessagingProviderException(callerSafeMessage, (int)sdk.Error.StatusCode, sdk);
        }

        if (ex is DuplicateProviderWriteException)
        {
            _logger.LogWarning("A duplicate Twilio write was blocked.");
            return new MessagingProviderException(callerSafeMessage, statusCode: null, ex);
        }

        if (ex is JsonException)
        {
            _logger.LogWarning("Twilio returned a response that could not be processed.");
            return new MessagingProviderException("The provider returned a response that could not be processed.", statusCode: null, ex);
        }

        if (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("Twilio was unreachable or the call was cancelled.");
            return new MessagingProviderException("The messaging provider is unreachable.", statusCode: null, ex);
        }

        _logger.LogWarning("Unexpected Twilio integration failure.");
        return new MessagingProviderException(callerSafeMessage, statusCode: null, ex);
    }

    private static string? PageTokenFrom(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?');
        var query = queryIndex >= 0 ? nextPageUri[(queryIndex + 1)..] : nextPageUri;
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2
                && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }
}
