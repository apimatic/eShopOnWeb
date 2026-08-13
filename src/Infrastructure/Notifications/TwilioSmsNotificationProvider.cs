using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio-backed implementation of <see cref="ISmsNotificationProvider"/>. This is the only class that
/// talks to the Twilio SDK. Every provider failure is translated into a caller-safe
/// <see cref="NotificationProviderException"/>; a raw provider body — which can contain a shopper's
/// number — is never surfaced or logged.
/// </summary>
public class TwilioSmsNotificationProvider : ISmsNotificationProvider
{
    // Reconciliation paging bounds. Volume is tiny in practice; these are backstops so the page loop
    // never depends solely on the provider's own stop signal.
    private const long ReconciliationPageSize = 1000;
    private const int MaxReconciliationPages = 50;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsNotificationProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
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
                ct: cancellationToken);

            if (response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return PhoneNumberValidationResult.Valid(response.PhoneNumber!);
            }

            return PhoneNumberValidationResult.Invalid("The number is not a valid, reachable destination.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup answers "not a valid number" with a 404 — that is an outcome, not an outage.
            return PhoneNumberValidationResult.Invalid("The number is not a valid, reachable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderError("validate the number", ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse("validate the number", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable("validate the number", ex);
        }
    }

    public Task<ProviderMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default) =>
        GuardAsync("send the message", async () =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
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
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: cancellationToken);

            return MapMessage(message);
        });

    public Task<ProviderMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default) =>
        GuardAsync("schedule the message", async () =>
        {
            // Scheduling is Messaging-Service-only: send with the Messaging Service SID (not a plain From),
            // ScheduleType=Fixed and the future send-at time. The provider holds and sends it.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
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
                ct: cancellationToken);

            return MapMessage(message);
        });

    public Task<ProviderMessage> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default) =>
        GuardAsync("cancel the scheduled message", async () =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);

            return MapMessage(message);
        });

    public Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken cancellationToken = default) =>
        GuardAsync("read the message", async () =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: cancellationToken);

            return MapMessage(message);
        });

    public Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default) =>
        GuardAsync("dispose of the message content", async () =>
        {
            // Redact only the body (empty string). The record and its final status survive at the provider.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: cancellationToken);

            return true;
        });

    public Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        GuardAsync("list messages for reconciliation", async () =>
        {
            var results = new List<ProviderMessage>();
            var page = 0;
            while (page < MaxReconciliationPages)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,           // only this application's own sending number
                    dateSent: null,
                    dateSentQuery: to,                    // wire DateSent<  (on/before, upper bound)
                    dateSentQueryQuery: from,             // wire DateSent>  (on/after, lower bound)
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: null,
                    ct: cancellationToken);

                var messages = response.Messages;
                if (messages is null || messages.Count == 0)
                {
                    break;
                }

                foreach (var message in messages)
                {
                    results.Add(MapMessage(message));
                }

                // Provider-supplied stop signal, plus the page cap above as a bound that does not depend on it.
                if (messages.Count < ReconciliationPageSize)
                {
                    break;
                }

                page++;
            }

            return (IReadOnlyList<ProviderMessage>)results;
        });

    private static ProviderMessage MapMessage(ApiV2010AccountMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status?.Value,
        ErrorCode = message.ErrorCode,
        From = message.From,
        To = message.To,
        DateSent = message.DateSent
    };

    /// <summary>Runs an SDK call and translates every failure mode into a caller-safe provider exception.</summary>
    private static async Task<T> GuardAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderError(operation, ex);
        }
        catch (JsonException ex)
        {
            throw UnreadableResponse(operation, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(operation, ex);
        }
    }

    // The messages below carry only the operation and (for API errors) the HTTP status — never the raw
    // provider body, which can contain a shopper's number.
    private static NotificationProviderException ProviderError(string operation, SdkException<RawError> ex) =>
        new($"The messaging provider rejected the request to {operation} (HTTP {(int)ex.Error.StatusCode}).", ex);

    private static NotificationProviderException UnreadableResponse(string operation, Exception ex) =>
        new($"The messaging provider returned a response to {operation} that could not be processed.", ex);

    private static NotificationProviderException Unreachable(string operation, Exception ex) =>
        new($"The messaging provider could not be reached to {operation}.", ex);
}
