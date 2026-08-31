using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio-backed ISmsProvider. Every SDK call is bounded by a whole-call budget and every
/// failure kind is converted to SmsProviderException at this boundary. Phone numbers and
/// credentials are never logged.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    public const string HttpClientName = "Twilio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        var response = await Bounded(async token =>
        {
            try
            {
                return await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                    ct: token);
            }
            catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
            {
                throw converted;
            }
        }, ct);

        if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
        {
            return new PhoneNumberValidationResult(true, response.PhoneNumber, null);
        }

        var reason = response.ValidationErrors is { Count: > 0 }
            ? string.Join(", ", response.ValidationErrors.Select(e => e.Value))
            : "The provider does not consider this a usable destination.";
        return new PhoneNumberValidationResult(false, null, reason);
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct = default)
        => CreateMessageAsync(to, body, schedule: null, ct);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
        => CreateMessageAsync(to, body, schedule: sendAt, ct);

    public async Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        var message = await Bounded(async token =>
        {
            try
            {
                return await _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: messageSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: token);
            }
            catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
            {
                throw converted;
            }
        }, ct);

        return new SmsSendResult(true, message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage);
    }

    public async Task<ProviderMessageState> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        var message = await Bounded(async token =>
        {
            try
            {
                return await _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: messageSid,
                    requestOptions: null,
                    ct: token);
            }
            catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
            {
                throw converted;
            }
        }, ct);

        return new ProviderMessageState(
            message.Sid ?? messageSid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            ParseDate(message.DateSent));
    }

    public async Task<SmsSendResult> RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        // Redaction at the provider is asynchronous: the update is accepted (HTTP 200) but
        // the body can remain retrievable for several seconds afterwards, and while that
        // redaction is still being applied further updates are refused (HTTP 409). Some
        // terminal states refuse the update outright (HTTP 404). So: attempt the update,
        // tolerate 404/409, then poll until the body is confirmed gone.
        var updateRefused = false;
        try
        {
            await Bounded(async token =>
            {
                try
                {
                    return await _client.Api20100401Message.UpdateMessage(
                        accountSid: _settings.AccountSid,
                        sid: messageSid,
                        body: "",
                        status: null,
                        requestOptions: null,
                        ct: token);
                }
                catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
                {
                    throw converted;
                }
            }, ct);
        }
        catch (SmsProviderException ex) when (ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Conflict)
        {
            updateRefused = true;
            _logger.LogInformation(
                "Provider declined the redaction update for message {MessageSid} with HTTP {StatusCode}; confirming content directly.",
                messageSid, (int)ex.StatusCode!.Value);
        }

        var state = await WaitForRedactionAsync(messageSid, ct);
        if (state is not null)
        {
            return new SmsSendResult(true, messageSid, state.Status, state.ErrorCode, null);
        }

        if (updateRefused)
        {
            // The provider will not redact this message and the text is still retrievable;
            // the only remaining way to make it unretrievable from the provider is to delete
            // the message record outright. eShop's own notification record keeps the fact it
            // was sent and its outcome.
            await Bounded(async token =>
            {
                try
                {
                    await _client.Api20100401Message.DeleteMessage(
                        accountSid: _settings.AccountSid,
                        sid: messageSid,
                        requestOptions: null,
                        ct: token);
                    return true;
                }
                catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
                {
                    throw converted;
                }
            }, ct);
            _logger.LogInformation("Message {MessageSid} deleted at the provider after redaction was refused.", messageSid);
            return new SmsSendResult(true, messageSid, null, null, null);
        }

        _logger.LogWarning("Message {MessageSid} still has retrievable content after redaction.", messageSid);
        return new SmsSendResult(false, messageSid, null, null,
            "The provider still returns content for this message.");
    }

    /// <summary>
    /// Polls the provider until the message body is gone. Returns the last observed state on
    /// success, null if the body was still retrievable when the confirmation budget ran out.
    /// </summary>
    private async Task<ProviderMessageState?> WaitForRedactionAsync(string messageSid, CancellationToken ct)
    {
        ProviderMessageState? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
            last = await GetMessageAsync(messageSid, ct);
            if (string.IsNullOrEmpty(last.Body))
            {
                return last;
            }
        }
        return null;
    }

    public async Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessageSummary>();
        int? page = null;

        for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
        {
            var response = await Bounded(async token =>
            {
                try
                {
                    return await _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,          // DateSent< : sent before
                        dateSentQueryQuery: from,   // DateSent> : sent after
                        pageSize: ListPageSize,
                        page: page,
                        pageToken: null,
                        requestOptions: null,
                        ct: token);
                }
                catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
                {
                    throw converted;
                }
            }, ct);

            if (response.Messages is not null)
            {
                results.AddRange(response.Messages.Select(m => new ProviderMessageSummary(
                    m.Sid ?? string.Empty,
                    m.To,
                    m.From,
                    m.Status?.Value,
                    m.ErrorCode,
                    ParseDate(m.DateSent))));
            }

            if (string.IsNullOrEmpty(response.NextPageUri) || response.Messages is not { Count: > 0 })
            {
                break;
            }

            var nextPage = (response.Page ?? 0) + 1;
            if (nextPage == page)
            {
                break; // no-progress guard
            }
            page = nextPage;
        }

        return results;
    }

    private async Task<SmsSendResult> CreateMessageAsync(string to, string body, DateTimeOffset? schedule, CancellationToken ct)
    {
        var message = await Bounded(async token =>
        {
            try
            {
                return await _client.Api20100401Message.CreateMessage(
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
                    scheduleType: schedule is null ? null : MessageEnumScheduleType.Fixed,
                    sendAt: schedule,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: schedule is null ? _settings.FromNumber : null,
                    fallbackFrom: null,
                    messagingServiceSid: schedule is null ? null : _settings.MessagingServiceSid,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    requestOptions: null,
                    ct: token);
            }
            catch (Exception ex) when (ConvertProviderError(ex, ct, out var converted))
            {
                throw converted;
            }
        }, ct);

        return new SmsSendResult(true, message.Sid, message.Status?.Value, message.ErrorCode, message.ErrorMessage);
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>
    /// The one conversion ladder for every SDK failure kind. Never carries provider response
    /// bodies (they can embed the destination number) — only the status and a safe message.
    /// Returns false for the caller's own cancellation, which must propagate as-is.
    /// </summary>
    private bool ConvertProviderError(Exception ex, CancellationToken ct, out SmsProviderException converted)
    {
        switch (ex)
        {
            case SmsProviderException:
                converted = null!;
                return false; // already converted — let the original propagate
            case SdkException<RawError> sdkEx:
                _logger.LogWarning("Twilio rejected a messaging call with HTTP {StatusCode}.", (int)sdkEx.Error.StatusCode);
                converted = new SmsProviderException(
                    $"The messaging provider rejected the request (HTTP {(int)sdkEx.Error.StatusCode}).",
                    sdkEx.Error.StatusCode, sdkEx);
                return true;
            case JsonException jsonEx:
                converted = new SmsProviderException(
                    "The messaging provider returned a response that could not be processed.", null, jsonEx);
                return true;
            case TaskCanceledException when ct.IsCancellationRequested:
                converted = null!;
                return false; // the caller cancelled — let it propagate
            case HttpRequestException or TaskCanceledException:
                converted = new SmsProviderException("The messaging provider could not be reached.", null, ex);
                return true;
            default:
                converted = null!;
                return false;
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
