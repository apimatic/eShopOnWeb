using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// The one place that talks to the Twilio SDK. Translates every provider failure — an error status
/// (<see cref="SdkException{RawError}"/>), a transport failure, a broken body (<see cref="JsonException"/>),
/// and a refused duplicate send — into <see cref="SmsProviderException"/>, carrying the HTTP status so a
/// boundary can map it deliberately. Never surfaces the provider's raw error body (which can echo the
/// destination number) and never logs the number.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsSender(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: cancellationToken);

            var isValid = response.Valid == true;
            var errors = response.ValidationErrors is null
                ? Array.Empty<string>()
                : response.ValidationErrors.Select(e => e.Value).ToArray();

            return new PhoneNumberValidationResult
            {
                IsValid = isValid,
                CanonicalNumber = isValid ? response.PhoneNumber : null,
                ValidationErrors = errors
            };
        }
        catch (SdkException<RawError> ex)
        {
            // Per the contract, a parseable-but-invalid (or unparseable) number may surface as a client error
            // rather than Valid=false. Treat a 4xx as "not a usable destination", not a provider outage.
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500)
            {
                return new PhoneNumberValidationResult { IsValid = false };
            }
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The phone-number provider is unreachable.", innerException: ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken)
        => Guarded(async () =>
        {
            // Immediate send from the configured number so the message is reconcilable by From.
            using var scope = new SendGuardScope();
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: cancellationToken);

            return ToSendResult(message);
        }, "The messaging provider is unreachable.", unknownOnTransport: true);

    public Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => Guarded(async () =>
        {
            // Scheduling is "for Messaging Services only" — queue it WITH the provider for the future.
            using var scope = new SendGuardScope();
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null, contentSid: null,
                ct: cancellationToken);

            return ToSendResult(message);
        }, "The messaging provider is unreachable.", unknownOnTransport: true);

    public Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken)
        => Guarded(async () =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);
            return true;
        }, "The messaging provider is unreachable.");

    public async Task<SmsMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken)
        => await Guarded(async () =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: messageSid, ct: cancellationToken);
            return new SmsMessageStatus
            {
                Status = message.Status?.Value,
                ErrorCode = message.ErrorCode,
                ErrorMessage = message.ErrorMessage
            };
        }, "The messaging provider is unreachable.");

    public Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken)
        => Guarded(async () =>
        {
            // Update the body to empty: blanks the stored text at the provider while keeping the record + status.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: string.Empty, status: null,
                ct: cancellationToken);
            return true;
        }, "The messaging provider is unreachable.");

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        => await Guarded(async () =>
        {
            var results = new List<ProviderMessageRecord>();
            string? pageToken = null;

            // Manual paging with a page cap that does not depend on the provider's cooperation.
            for (var pageCount = 0; pageCount < MaxReconciliationPages; pageCount++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // wire From — ask the provider for OUR number's messages only
                    dateSent: null,
                    dateSentQuery: toUtc,                // wire DateSent<  — range end (sent on/before)
                    dateSentQueryQuery: fromUtc,         // wire DateSent>  — range start (sent on/after)
                    pageSize: ReconciliationPageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: cancellationToken);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        results.Add(new ProviderMessageRecord
                        {
                            Sid = m.Sid,
                            Status = m.Status?.Value,
                            From = m.From,
                            DateSent = ParseDate(m.DateSent),
                            ErrorCode = m.ErrorCode
                        });
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }
                pageToken = ExtractPageToken(response.NextPageUri);
                if (pageToken is null)
                {
                    break;
                }
            }

            return (IReadOnlyList<ProviderMessageRecord>)results;
        }, "The messaging provider is unreachable.");

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status?.Value,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage
    };

    // Single translation of provider failures into the integration's own error type. The provider's raw error
    // body is NOT surfaced — only its HTTP status — because that body can contain the destination number.
    private static SmsProviderException Translate(SdkException<RawError> ex)
    {
        var status = ex.Error.StatusCode;
        return new SmsProviderException(
            $"The messaging provider rejected the request (HTTP {(int)status}).",
            status,
            ex);
    }

    private async Task<T> Guarded<T>(Func<Task<T>> call, string unreachableMessage, bool unknownOnTransport = false)
    {
        try
        {
            return await call();
        }
        catch (DuplicateSendBlockedException ex)
        {
            // A transport retry was refused; the first send may already have reached the provider once.
            throw new SmsProviderException(ex.Message, outcomeUnknown: true, innerException: ex);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException(unreachableMessage, innerException: ex, outcomeUnknown: unknownOnTransport);
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? ExtractPageToken(string nextPageUri)
    {
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(queryStart + 1)..].Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }
}
