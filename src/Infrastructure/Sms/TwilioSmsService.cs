using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
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

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Twilio implementation of <see cref="ISmsService"/>, built on the twilio-sdk plugin. It is the only type
/// that talks to Twilio. Every provider failure is translated to <see cref="SmsProviderException"/> with a
/// caller-safe message; a provider 4xx keeps its status so the boundary can map it back to a client error.
/// It never logs (nor puts into an exception message) the auth token or a destination number.
/// </summary>
public class TwilioSmsService : ISmsService
{
    // Twilio's maximum list page size.
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsService(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, ct: cancellationToken);

            var isValid = response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber);
            return new PhoneNumberValidationResult(isValid, isValid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup reports an unparseable / non-existent number as 404 — a determinable "not usable"
            // answer, not a provider outage.
            return new PhoneNumberValidationResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<SentSmsMessage> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null, ct: cancellationToken);

            return ToSentMessage(response.Sid, response.Status?.Value);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<SentSmsMessage> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        try
        {
            // Scheduling is Messaging-Service-only: use the messaging service SID, not a From number.
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: cancellationToken);

            return ToSentMessage(response.Sid, response.Status?.Value);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<SmsMessageState> GetMessageAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: cancellationToken);

            return new SmsMessageState(response.Status?.Value ?? string.Empty, response.ErrorCode, response.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            // Setting the body to empty redacts the message text at the provider; the record survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<ProviderSmsRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var records = new List<ProviderSmsRecord>();
        var page = 0;
        string? pageToken = null;

        try
        {
            while (true)
            {
                // Ask the provider only for messages sent FROM the configured number within the range —
                // the account carries other traffic that is not this application's.
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,          // wire DateSent<  (upper bound)
                    dateSentQueryQuery: from,   // wire DateSent>  (lower bound)
                    pageSize: ListPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: cancellationToken);

                if (response.Messages != null)
                {
                    foreach (var message in response.Messages)
                    {
                        records.Add(new ProviderSmsRecord(
                            message.Sid ?? string.Empty,
                            message.Status?.Value,
                            message.To,
                            message.From,
                            ParseProviderDate(message.DateSent),
                            message.Body));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                    break;

                if (!TryReadNextPage(response.NextPageUri, out page, out pageToken))
                    break;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (Exception ex) when (IsTransportOrParseFailure(ex))
        {
            throw ToProviderException(ex);
        }

        return records;
    }

    // ---------------------------------------------------------------- helpers

    private static SentSmsMessage ToSentMessage(string? sid, string? status)
    {
        if (string.IsNullOrEmpty(sid))
            throw new SmsProviderException("The messaging provider did not return a message identifier.");
        return new SentSmsMessage(sid, status ?? string.Empty);
    }

    private static bool IsTransportOrParseFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException;

    private static SmsProviderException ToProviderException(Exception ex) => ex is JsonException
        ? new SmsProviderException("The messaging provider returned a response that could not be processed.", null, ex)
        : new SmsProviderException("The messaging provider is currently unreachable.", null, ex);

    /// <summary>
    /// Translate a provider error response. The provider's HTTP status is carried through (so a 4xx maps back
    /// to a client error); the raw provider body is deliberately not surfaced — it could echo a destination
    /// number — so only a safe, generic message is used.
    /// </summary>
    private static SmsProviderException Translate(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        return new SmsProviderException("The messaging provider returned an error.", status, ex);
    }

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Read the Page and PageToken out of the provider's NextPageUri to advance paging correctly.</summary>
    private static bool TryReadNextPage(string nextPageUri, out int page, out string? pageToken)
    {
        page = 0;
        pageToken = null;

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
            return false;

        var query = nextPageUri[(queryStart + 1)..];
        var found = false;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                continue;
            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);

            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
                found = true;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
                found = true;
            }
        }

        return found;
    }
}
