using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// The only place the Twilio SDK is used. Translates every provider/transport/parse failure into
/// <see cref="SmsGatewayException"/>. Never logs the phone number or the message body.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 200;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<PhoneNumberLookup> LookupNumberAsync(string phoneNumber, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            // Lookup is served from its own host (not the messaging base-URL override).
            // The number is in the URL PATH, which the SDK logger does NOT redact — so suppress
            // all logging for this one call (LogLevel.None makes NeedsUrl false; the URL is never
            // computed or written at any level). The shopper's number therefore never reaches a log.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                requestOptions: new RequestOptions { LogLevel = LogLevel.None }, ct: c);
            return new PhoneNumberLookup(response.Valid == true, response.PhoneNumber);
        }, "phone-number lookup", ct);

    public Task<SentMessage> SendAsync(string toE164, string body, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null, ct: c);
            return ToSentMessage(message);
        }, "send message", ct);

    public Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            // Scheduling requires a Messaging Service and scheduleType=fixed; 'from' must be null.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null,
                contentSid: null, ct: c);
            return ToSentMessage(message);
        }, "schedule message", ct);

    public Task<SentMessage> FetchAsync(string sid, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: sid, ct: c);
            return ToSentMessage(message);
        }, "fetch message", ct);

    public Task<SentMessage> CancelScheduledAsync(string sid, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: sid, body: null,
                status: MessageEnumUpdateStatus.Canceled, ct: c);
            return ToSentMessage(message);
        }, "cancel scheduled message", ct);

    public Task RedactContentAsync(string sid, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            // Empty body redacts the message content at the provider; the record itself survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: sid, body: string.Empty, status: null, ct: c);
            return true;
        }, "redact message content", ct);

    public Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        InvokeAsync(async c =>
        {
            var results = new List<ProviderMessage>();
            var page = 0;
            while (page < MaxReconciliationPages)
            {
                c.ThrowIfCancellationRequested();
                // Provider-side filter by sender (this app's configured number) AND date range:
                //   dateSentQueryQuery -> wire `DateSent>` (on/after)  = range start
                //   dateSentQuery      -> wire `DateSent<` (on/before) = range end
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: null,
                    ct: c);

                var messages = response.Messages;
                if (messages is null || messages.Count == 0)
                    break;

                foreach (var m in messages)
                {
                    if (!string.IsNullOrEmpty(m.Sid))
                        results.Add(ToProviderMessage(m));
                }

                if (string.IsNullOrEmpty(response.NextPageUri) || messages.Count < ReconciliationPageSize)
                    break;
                page++;
            }

            return (IReadOnlyList<ProviderMessage>)results;
        }, "list messages", ct);

    // ---- mapping & boundary --------------------------------------------------------------------

    private static SentMessage ToSentMessage(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
            throw new SmsGatewayException("The messaging provider did not return a message identifier.");
        return new SentMessage(
            message.Sid!,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            ParseDate(message.DateSent));
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message) =>
        new(
            message.Sid!,
            message.To,
            message.From,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            ParseDate(message.DateSent));

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Single boundary: Case B <see cref="SdkException{TError}"/> of <see cref="RawError"/> for
    /// provider errors; <see cref="System.Text.Json.JsonException"/> for a 2xx/error body that does
    /// not match the model; transport failures for an unreachable provider. Our own cancellation is
    /// rethrown, not wrapped.
    /// </summary>
    private static async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> operation, string action, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsGatewayException($"The messaging provider rejected the {action} request.", ex.Error.StatusCode, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SmsGatewayException($"The messaging provider returned a response for the {action} request that could not be processed.", null, ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException($"The messaging provider is unreachable for the {action} request.", null, ex);
        }
    }
}
