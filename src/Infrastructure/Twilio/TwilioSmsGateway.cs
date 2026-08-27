using System;
using System.Collections.Generic;
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
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio messaging-API implementation of <see cref="ISmsGateway"/>. Every call is bounded by a
/// whole-call budget, every failure is translated to <see cref="SmsProviderException"/>, and
/// message-create writes run inside a send-guard scope so a transport retry can never produce a
/// duplicate SMS. Destination numbers and provider error bodies (which can echo the number) are
/// never logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            using var writeScope = TwilioSendGuardHandler.BeginWriteScope();
            try
            {
                var message = await _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toNumber,
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
                    scheduleType: null,
                    sendAt: null,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: null,
                    fallbackFrom: null,
                    messagingServiceSid: _settings.MessagingServiceSid,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: token);

                return new SmsSendResult
                {
                    MessageSid = message.Sid,
                    Status = message.Status?.Value,
                    ErrorCode = message.ErrorCode,
                    ErrorMessage = message.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                throw TranslateWrite(ex, "send");
            }
        }, ct);
    }

    public Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            using var writeScope = TwilioSendGuardHandler.BeginWriteScope();
            try
            {
                // Scheduling is Messaging-Services-only, so the messaging service SID is the sender identity.
                var message = await _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toNumber,
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
                    scheduleType: MessageEnumScheduleType.Fixed,
                    sendAt: sendAt,
                    sendAsMms: null,
                    contentVariables: null,
                    riskCheck: null,
                    from: null,
                    fallbackFrom: null,
                    messagingServiceSid: _settings.MessagingServiceSid,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: token);

                return new SmsSendResult
                {
                    MessageSid = message.Sid,
                    Status = message.Status?.Value,
                    ErrorCode = message.ErrorCode,
                    ErrorMessage = message.ErrorMessage
                };
            }
            catch (Exception ex)
            {
                throw TranslateWrite(ex, "schedule");
            }
        }, ct);
    }

    public Task<SmsMessageState> CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            try
            {
                var message = await _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: messageSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: token);

                return ToState(message);
            }
            catch (Exception ex)
            {
                throw TranslateRead(ex, "cancel");
            }
        }, ct);
    }

    public Task<SmsMessageState> GetStateAsync(string messageSid, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            try
            {
                var message = await _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: messageSid,
                    ct: token);

                return ToState(message);
            }
            catch (Exception ex)
            {
                throw TranslateRead(ex, "fetch");
            }
        }, ct);
    }

    public Task RedactBodyAsync(string messageSid, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            try
            {
                // Redaction: empty body. The message record (SID, status, dates, parties) survives.
                await _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: messageSid,
                    body: "",
                    status: null,
                    ct: token);

                return true;
            }
            catch (Exception ex)
            {
                throw TranslateRead(ex, "redact");
            }
        }, ct);
    }

    public Task<ProviderMessageListResult> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        return Bounded(async token =>
        {
            var result = new ProviderMessageListResult();
            var records = new List<ProviderMessageRecord>();

            // The provider's DateSent filters are GMT and date-granular with STRICT </> semantics,
            // so widen the window by one day on each side to cover the whole [from, to] range.
            // The sender filter is passed to the provider — the account carries other traffic.
            var dateSentBefore = new DateTimeOffset(to.Date.AddDays(1), TimeSpan.Zero);
            var dateSentAfter = new DateTimeOffset(from.Date.AddDays(-1), TimeSpan.Zero);

            string? pageToken = null;
            var pages = 0;
            string? nextPageUri;

            try
            {
                do
                {
                    var page = await _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: dateSentBefore,
                        dateSentQueryQuery: dateSentAfter,
                        pageSize: ListPageSize,
                        page: null,
                        pageToken: pageToken,
                        ct: token);

                    if (page.Messages != null)
                    {
                        foreach (var message in page.Messages)
                        {
                            records.Add(new ProviderMessageRecord
                            {
                                MessageSid = message.Sid,
                                To = message.To,
                                Status = message.Status?.Value,
                                ErrorCode = message.ErrorCode,
                                ErrorMessage = message.ErrorMessage,
                                DateSent = ParseProviderDate(message.DateSent),
                                DateCreated = ParseProviderDate(message.DateCreated)
                            });
                        }
                    }

                    nextPageUri = page.NextPageUri;
                    pageToken = ExtractPageToken(nextPageUri);

                    // A page cap is the bound that does not depend on the provider's cooperation;
                    // hitting it is surfaced to the caller, never silently truncated.
                    if (nextPageUri != null && ++pages >= MaxListPages)
                    {
                        result.Truncated = true;
                        break;
                    }
                }
                while (nextPageUri != null);
            }
            catch (Exception ex)
            {
                throw TranslateRead(ex, "list");
            }

            result.Messages = records;
            return result;
        }, ct);
    }

    private static SmsMessageState ToState(TwilioSdk.Models.ApiV2010AccountMessage message)
    {
        return new SmsMessageState
        {
            MessageSid = message.Sid,
            Status = message.Status?.Value,
            ErrorCode = message.ErrorCode,
            ErrorMessage = message.ErrorMessage
        };
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri) || !Uri.TryCreate(nextPageUri, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return query["PageToken"];
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>
    /// Write-path translation: a transport failure or an unreadable success body means the write
    /// may have reached the provider — the outcome is unknown, not failed.
    /// </summary>
    private static Exception TranslateWrite(Exception ex, string operation)
    {
        switch (ex)
        {
            case SmsProviderException:
                return ex;
            case TwilioDuplicateSendGuardException guard:
                return new SmsProviderException(
                    $"The {operation} request may already have reached the provider; a duplicate send was blocked.",
                    null, guard, outcomeUnknown: true);
            case SdkException<RawError> sdk:
                return new SmsProviderException(
                    $"The messaging provider rejected the {operation} request (HTTP {(int)sdk.Error.StatusCode}).",
                    sdk.Error.StatusCode, sdk);
            case JsonException json:
                return new SmsProviderException(
                    "The provider returned a response that could not be processed.", null, json, outcomeUnknown: true);
            case HttpRequestException http:
                return new SmsProviderException(
                    "The messaging provider could not be reached.", null, http, outcomeUnknown: true);
            case TaskCanceledException timeout:
                return new SmsProviderException(
                    "The messaging provider did not answer in time.", null, timeout, outcomeUnknown: true);
            default:
                return ex;
        }
    }

    /// <summary>Read-path translation: nothing was written, so failures are plain failures.</summary>
    private static Exception TranslateRead(Exception ex, string operation)
    {
        switch (ex)
        {
            case SmsProviderException:
                return ex;
            case SdkException<RawError> sdk:
                return new SmsProviderException(
                    $"The messaging provider rejected the {operation} request (HTTP {(int)sdk.Error.StatusCode}).",
                    sdk.Error.StatusCode, sdk);
            case JsonException json:
                return new SmsProviderException(
                    "The provider returned a response that could not be processed.", null, json);
            case HttpRequestException http:
                return new SmsProviderException("The messaging provider could not be reached.", null, http);
            case TaskCanceledException timeout:
                return new SmsProviderException("The messaging provider did not answer in time.", null, timeout);
            default:
                return ex;
        }
    }
}
