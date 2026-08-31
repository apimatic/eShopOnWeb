using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>Provider-owned state for one message, projected off the SDK record.</summary>
public sealed record ProviderMessage(
    string Sid,
    string Status,
    string? To,
    string? From,
    int? ErrorCode,
    string? ErrorMessage,
    string? DateSent);

public sealed record NumberValidationResult(
    bool IsValid,
    string? CanonicalNumber,
    IReadOnlyList<string> ValidationErrors);

/// <summary>
/// Low-level wrapper over the Twilio SDK messaging and lookup APIs. Every call is bounded by
/// a whole-call deadline and funnelled through one error ladder that converts SDK failures
/// into <see cref="MessagingException"/>. Destination numbers and auth material are never
/// logged here.
/// </summary>
public class TwilioMessagingService
{
    private const int MaxListPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioMessagingService> _logger;

    public TwilioMessagingService(TwilioSdkClient client, IOptions<TwilioOptions> options,
        ILogger<TwilioMessagingService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NumberValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            try
            {
                var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                    ct: token);

                var isValid = response.Valid == true;
                var errors = response.ValidationErrors?
                    .Select(e => e.Value)
                    .ToArray() ?? Array.Empty<string>();
                return new NumberValidationResult(isValid, isValid ? response.PhoneNumber : null, errors);
            }
            catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode is >= 400 and < 500)
            {
                // The provider rejected the number itself — that is a "not usable" verdict,
                // not an outage.
                return new NumberValidationResult(false, null, new[] { "rejected-by-provider" });
            }
        }, ct);
    }

    public Task<ProviderMessage> SendMessageAsync(string to, string body, CancellationToken ct)
        => CreateMessageAsync(to, body, scheduleAt: null, ct);

    public Task<ProviderMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct)
        => CreateMessageAsync(to, body, scheduleAt: sendAt, ct);

    private async Task<ProviderMessage> CreateMessageAsync(string to, string body, DateTimeOffset? scheduleAt, CancellationToken ct)
    {
        if (scheduleAt is not null && string.IsNullOrWhiteSpace(_options.MessagingServiceSid))
        {
            throw new MessagingException(
                "Twilio:MessagingServiceSid is not configured; scheduled messages require a messaging service.",
                null);
        }

        // Exactly one sender identity per message: scheduling requires the messaging service;
        // immediate sends prefer the configured sending number so reconciliation by
        // Twilio:FromNumber sees them.
        string? from = scheduleAt is null && !string.IsNullOrWhiteSpace(_options.FromNumber)
            ? _options.FromNumber
            : null;
        string? messagingServiceSid = from is null ? _options.MessagingServiceSid : null;

        return await Bounded(async token =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
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
                scheduleType: scheduleAt is null ? null : MessageEnumScheduleType.Fixed,
                sendAt: scheduleAt,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: from,
                fallbackFrom: null,
                messagingServiceSid: messagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: token);

            return Project(message);
        }, ct);
    }

    public async Task<ProviderMessage> GetMessageAsync(string sid, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                ct: token);
            return Project(message);
        }, ct);
    }

    /// <summary>
    /// Cancels a scheduled message that has not gone out yet. Returns the message's outcome
    /// after the operation; if the message is already past the cancellable state the provider
    /// rejection is surfaced as a <see cref="MessagingException"/> carrying the provider status.
    /// </summary>
    public async Task<ProviderMessage> CancelScheduledMessageAsync(string sid, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            var current = await _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                ct: token);

            if (current.Status != MessageEnumStatus.Scheduled)
            {
                return Project(current);
            }

            var cancelled = await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token);
            return Project(cancelled);
        }, ct);
    }

    /// <summary>
    /// Redacts the message body at the provider; the record and its delivery outcome survive.
    /// </summary>
    public async Task<ProviderMessage> RedactMessageBodyAsync(string sid, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: sid,
                body: string.Empty,
                status: null,
                ct: token);
            return Project(message);
        }, ct);
    }

    /// <summary>
    /// Lists the provider's own record of messages sent from this application's configured
    /// sending number within [from, to], paging through the whole range.
    /// </summary>
    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return await Bounded(async token =>
        {
            var results = new List<ProviderMessage>();
            var page = 0;
            while (true)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,        // DateSent<
                    dateSentQueryQuery: from, // DateSent>
                    pageSize: 100,
                    page: page,
                    pageToken: null,
                    ct: token);

                if (response.Messages is not null)
                {
                    results.AddRange(response.Messages.Select(Project));
                }

                page++;
                if (response.NextPageUri is null || page >= MaxListPages)
                {
                    if (page >= MaxListPages && response.NextPageUri is not null)
                    {
                        _logger.LogWarning("Reconciliation listing hit the page cap of {MaxPages}; the report is truncated.", MaxListPages);
                    }
                    return results;
                }
            }
        }, ct);
    }

    private static ProviderMessage Project(TwilioSdk.Models.ApiV2010AccountMessage message)
    {
        return new ProviderMessage(
            Sid: message.Sid ?? string.Empty,
            Status: message.Status?.Value ?? string.Empty,
            To: message.To,
            From: message.From,
            ErrorCode: message.ErrorCode,
            ErrorMessage: message.ErrorMessage,
            DateSent: message.DateSent);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.CallTimeoutSeconds));
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio API rejected a request with status {StatusCode}.", (int)ex.Error.StatusCode);
            throw new MessagingException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Twilio returned a response that could not be deserialized.");
            throw new MessagingException("The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning("Twilio could not be reached or a call timed out.");
            throw new MessagingException("The messaging provider could not be reached.", null, ex);
        }
    }
}
