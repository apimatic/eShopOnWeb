using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio-backed implementation of <see cref="ISmsProvider"/>. Every Twilio interaction lives here,
/// behind the application-core port. Shopper numbers are never written to logs by this adapter.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsProvider(TwilioSdkClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public async Task<PhoneNumberValidation> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
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

            var isValid = response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber);
            return new PhoneNumberValidation(isValid, isValid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error?.StatusCode == HttpStatusCode.NotFound)
        {
            // A number the provider cannot even parse/locate is not a usable destination — reject it
            // here rather than treating it as an outage.
            return new PhoneNumberValidation(false, null);
        }
    }

    public async Task<SentMessage> SendAsync(string toE164, string body, CancellationToken cancellationToken = default)
    {
        // Send immediately from the application's own configured sending number so the message is
        // attributable to Twilio:FromNumber (which reconciliation relies on).
        var message = await CreateMessageAsync(
            toE164: toE164,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            scheduleType: null,
            sendAt: null,
            cancellationToken: cancellationToken);

        return new SentMessage(RequireSid(message), MapStatus(message.Status), message.ErrorCode);
    }

    public async Task<SentMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling requires a Messaging Service; the provider holds and later sends the message.
        var message = await CreateMessageAsync(
            toE164: toE164,
            body: body,
            from: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken: cancellationToken);

        return new SentMessage(RequireSid(message), MapStatus(message.Status), message.ErrorCode);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: cancellationToken);
    }

    public async Task<MessageDeliveryState> FetchStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var message = await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            ct: cancellationToken);

        return new MessageDeliveryState(MapStatus(message.Status), message.ErrorCode);
    }

    public async Task DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Redact the body on the provider's side so its text is no longer retrievable there, while the
        // message record and its status survive.
        await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: string.Empty,
            status: null,
            ct: cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var page = 0;

        while (true)
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,          // only this application's own sending number
                dateSent: null,
                dateSentQuery: to,                   // DateSent<= upper bound
                dateSentQueryQuery: from,            // DateSent>= lower bound
                pageSize: 1000,
                page: page,
                pageToken: null,
                ct: cancellationToken);

            var messages = response.Messages;
            if (messages is not null)
            {
                foreach (var message in messages)
                {
                    if (string.IsNullOrEmpty(message.Sid))
                    {
                        continue;
                    }

                    results.Add(new ProviderMessage(
                        message.Sid,
                        MapStatus(message.Status),
                        ParseDate(message.DateSent),
                        message.ErrorCode));
                }
            }

            // Cover the whole range: keep paging while the provider reports another page.
            if (string.IsNullOrEmpty(response.NextPageUri) || messages is null || messages.Count == 0)
            {
                break;
            }

            page++;
        }

        return results;
    }

    private Task<ApiV2010AccountMessage> CreateMessageAsync(
        string toE164,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        return _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
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
            ct: cancellationToken);
    }

    private static string RequireSid(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new InvalidOperationException("The provider accepted the message but returned no identifier.");
        }

        return message.Sid;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static NotificationDeliveryStatus MapStatus(MessageEnumStatus? status)
    {
        // Map the provider's wire status string onto our own delivery outcome.
        return status?.Value switch
        {
            "queued" => NotificationDeliveryStatus.Queued,
            "sending" => NotificationDeliveryStatus.Sending,
            "sent" => NotificationDeliveryStatus.Sent,
            "delivered" => NotificationDeliveryStatus.Delivered,
            "undelivered" => NotificationDeliveryStatus.Undelivered,
            "failed" => NotificationDeliveryStatus.Failed,
            "accepted" => NotificationDeliveryStatus.Accepted,
            "scheduled" => NotificationDeliveryStatus.Scheduled,
            "canceled" => NotificationDeliveryStatus.Canceled,
            "partially_delivered" => NotificationDeliveryStatus.PartiallyDelivered,
            "read" => NotificationDeliveryStatus.Read,
            "receiving" => NotificationDeliveryStatus.Receiving,
            "received" => NotificationDeliveryStatus.Received,
            _ => NotificationDeliveryStatus.Unknown
        };
    }
}
