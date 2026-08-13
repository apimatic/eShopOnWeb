using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Twilio-backed implementation of <see cref="INotificationGateway"/>. All messaging goes through the
/// APIMatic-generated Twilio SDK. Destination numbers and message bodies are never logged, and the
/// auth token lives only inside the SDK client's credentials.
/// </summary>
public class TwilioNotificationGateway : INotificationGateway
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioNotificationGateway> _logger;

    public TwilioNotificationGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioNotificationGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string rawNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: cancellationToken);

            var isValid = response.Valid ?? false;
            var canonical = isValid ? response.PhoneNumber : null;
            return new PhoneValidationResult(isValid && !string.IsNullOrWhiteSpace(canonical), canonical);
        }
        catch (SdkException<RawError> ex) when (
            ex.Error.StatusCode == HttpStatusCode.NotFound ||
            ex.Error.StatusCode == HttpStatusCode.BadRequest)
        {
            // Twilio Lookup answers 404/400 for a number it cannot resolve or parse: not a usable destination.
            return new PhoneValidationResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("validate phone number", ex);
        }
    }

    public async Task<SentMessageResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid, to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null, body: body,
                mediaUrl: null, contentSid: null,
                ct: cancellationToken);

            return ToSentResult(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("send message", ex);
        }
    }

    public async Task<SentMessageResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        try
        {
            // Scheduling requires a Messaging Service (not a bare From number) plus ScheduleType=fixed + SendAt.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid, to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid, body: body,
                mediaUrl: null, contentSid: null,
                ct: cancellationToken);

            return ToSentResult(message);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("schedule message", ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("cancel scheduled message", ex);
        }
    }

    public async Task<MessageDeliveryState?> FetchStateAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                ct: cancellationToken);

            return new MessageDeliveryState(
                NormalizeStatus(message.Status),
                message.ErrorCode,
                message.ErrorMessage,
                ParseDate(message.DateSent));
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("fetch message", ex);
        }
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            // Setting the body to empty string redacts the content at the provider while the record survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: providerMessageSid,
                body: string.Empty, status: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("redact message content", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessageRecord>();
        int? page = null;
        string? pageToken = null;

        // Ask the provider only for messages sent FROM our configured number, within the range.
        // dateSentQueryQuery == DateSent> (lower bound); dateSentQuery == DateSent< (upper bound).
        for (var guard = 0; guard < 1000; guard++)
        {
            ListMessageResponse response;
            try
            {
                response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid, to: null, from: _settings.FromNumber,
                    dateSent: null, dateSentQuery: to, dateSentQueryQuery: from,
                    pageSize: 1000, page: page, pageToken: pageToken,
                    ct: cancellationToken);
            }
            catch (SdkException<RawError> ex)
            {
                throw ToGatewayException("list messages", ex);
            }

            var messages = response.Messages;
            if (messages is not null)
            {
                foreach (var m in messages)
                {
                    results.Add(new ProviderMessageRecord(
                        m.Sid ?? string.Empty, m.To, m.From,
                        NormalizeStatus(m.Status), ParseDate(m.DateSent), m.ErrorCode));
                }
            }

            if (!TryGetNextPage(response.NextPageUri, out page, out pageToken))
            {
                break;
            }
        }

        return results;
    }

    // ---- mapping helpers --------------------------------------------------

    private static SentMessageResult ToSentResult(ApiV2010AccountMessage message) => new(
        message.Sid ?? string.Empty,
        NormalizeStatus(message.Status),
        message.ErrorCode,
        message.ErrorMessage,
        ParseDate(message.DateSent));

    private static string NormalizeStatus(MessageEnumStatus? status)
    {
        if (status is null)
        {
            return "unknown";
        }

        // MessageEnumStatus is a StringEnum wrapper, not a C# enum: its members are static readonly
        // instances (not compile-time constants), so they cannot appear as switch patterns. The
        // underlying wire string is exposed via .Value ("queued", "sent", "partially_delivered", ...).
        return status.Value switch
        {
            "queued" => NotificationStatus.Queued,
            "sending" => NotificationStatus.Sending,
            "sent" => NotificationStatus.Sent,
            "delivered" => NotificationStatus.Delivered,
            "undelivered" => NotificationStatus.Undelivered,
            "failed" => NotificationStatus.Failed,
            "canceled" => NotificationStatus.Canceled,
            "scheduled" => NotificationStatus.Scheduled,
            "partially_delivered" => "partially_delivered",
            _ => status.Value.ToLowerInvariant()
        };
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

    private static bool TryGetNextPage(string? nextPageUri, out int? page, out string? pageToken)
    {
        page = null;
        pageToken = null;
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return false;
        }

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
        {
            return false;
        }

        NameValueCollection query = HttpUtility.ParseQueryString(nextPageUri.Substring(queryStart + 1));
        var pageValue = query["Page"];
        if (int.TryParse(pageValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage))
        {
            page = parsedPage;
        }
        pageToken = query["PageToken"];

        // Only continue if we actually advanced.
        return page is not null || !string.IsNullOrEmpty(pageToken);
    }

    /// <summary>
    /// Turns an SDK error into a number-free, body-free gateway exception. Only the HTTP status and
    /// Twilio's numeric error code are surfaced — never the provider's message text, which can echo
    /// the destination number.
    /// </summary>
    private SmsGatewayException ToGatewayException(string action, SdkException<RawError> ex)
    {
        int? twilioCode = null;
        try
        {
            var body = ex.Error.ReadAsJson<TwilioErrorBody>();
            twilioCode = body?.Code;
        }
        catch
        {
            // Ignore: the error body may not be JSON, or may not match the shape.
        }

        var codePart = twilioCode.HasValue ? $", provider code {twilioCode.Value}" : string.Empty;
        var message = $"Messaging provider could not {action} (HTTP {(int)ex.Error.StatusCode}{codePart}).";
        _logger.LogWarning("Twilio error during {0}: HTTP {1}{2}", action, (int)ex.Error.StatusCode, codePart);
        return new SmsGatewayException(message);
    }

    /// <summary>Minimal shape of a Twilio error body; only the numeric code is read (message may contain PII).</summary>
    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }

        [JsonPropertyName("status")]
        public int? Status { get; init; }
    }
}
