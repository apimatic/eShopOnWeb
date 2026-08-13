using System;
using System.Collections.Generic;
using System.Globalization;
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
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The <see cref="ISmsProvider"/> implementation over the Twilio .NET SDK. Every SDK call is bounded by a
/// total call budget and wrapped so that Twilio's exception types (API errors, transport failures, and
/// deserialization failures) are all translated into the single <see cref="SmsProviderException"/>. A
/// destination number is never written to a log here.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly TimeSpan _callBudget;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
        _callBudget = TimeSpan.FromSeconds(_settings.CallBudgetSeconds <= 0 ? 30 : _settings.CallBudgetSeconds);
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        try
        {
            // Lookup runs against the lookups host (NOT governed by Twilio:BaseUrl). Only phoneNumber is
            // required; every other parameter is passed explicitly as null (named args).
            var lookup = await InvokeAsync(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: token), "number validation", ct);

            // Only accept when the provider says the number is valid and hands back its canonical form.
            if (lookup.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new PhoneNumberValidationResult(true, lookup.PhoneNumber);
            }

            return new PhoneNumberValidationResult(false, null);
        }
        catch (SmsProviderException ex) when (ex.StatusCode is >= 400 and < 500)
        {
            // A number the provider cannot parse comes back as a 4xx — treat that as "not a usable
            // destination" rather than surfacing it as a provider outage (defensive per the contract sheet).
            return new PhoneNumberValidationResult(false, null);
        }
    }

    public async Task<SentSmsMessage> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        // Variant: explicit From number, so the message is reconcilable by Twilio:FromNumber.
        var message = await InvokeAsync(token => _client.Api20100401Message.CreateMessage(
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
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            ct: token), "send", ct);

        return ToSentMessage(message, "send");
    }

    public async Task<SentSmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        // Scheduling is Messaging-Services-only: scheduleType Fixed + sendAt + MessagingServiceSid, no From.
        var message = await InvokeAsync(token => _client.Api20100401Message.CreateMessage(
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
            ct: token), "schedule", ct);

        return ToSentMessage(message, "schedule");
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        // Cancel a not-yet-sent message: status -> Canceled, body left null.
        await InvokeAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: token), "cancel scheduled message", ct);
    }

    public async Task<string?> FetchStatusAsync(string messageSid, CancellationToken ct = default)
    {
        var message = await InvokeAsync(token => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            ct: token), "status fetch", ct);

        return message.Status?.Value;
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken ct = default)
    {
        // Redact the body only (body -> ""): the text becomes non-retrievable while the send-record and its
        // status survive. (A whole-resource delete would destroy the send-record too — deliberately not used.)
        await InvokeAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: string.Empty,
            status: null,
            ct: token), "content disposal", ct);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessageRecord>();
        var page = 0;

        // Filter server-side by our sending number and the date range. Mapping per the contract sheet:
        //   dateSentQueryQuery = DateSent> (lower bound / range start)
        //   dateSentQuery      = DateSent< (upper bound / range end)
        while (page < MaxReconciliationPages)
        {
            var currentPage = page;
            var response = await InvokeAsync(token => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: ReconciliationPageSize,
                page: currentPage,
                pageToken: null,
                ct: token), "reconciliation list", ct);

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    results.Add(new ProviderMessageRecord(
                        message.Sid,
                        message.To,
                        message.From,
                        message.Status?.Value,
                        ParseDate(message.DateSent),
                        message.Body));
                }
            }

            // Stop when the provider signals no further page. The page cap above is the backstop that keeps
            // this from ever depending solely on the provider's stop condition.
            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            page++;
        }

        return results;
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private SentSmsMessage ToSentMessage(ApiV2010AccountMessage message, string action)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            // A 2xx with no identifier is unusable — treat as a provider failure so the caller records it.
            throw new SmsProviderException($"The messaging provider returned no identifier for the {action} request.");
        }

        return new SentSmsMessage(message.Sid, message.Status?.Value);
    }

    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, string action, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_callBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            // API error (non-2xx). Carry the status so callers can tell a 4xx rejection from a 5xx outage.
            // Only a caller-safe message and the status leave here — never the raw provider body.
            throw new SmsProviderException(
                $"The messaging provider rejected the {action} request (HTTP {(int)ex.Error.StatusCode}).",
                (int)ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model — outcome genuinely unknown.
            throw new SmsProviderException(
                $"The messaging provider returned a response to the {action} request that could not be processed.", ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // the caller cancelled — propagate as cancellation, not as a provider failure
        }
        catch (OperationCanceledException ex)
        {
            throw new SmsProviderException(
                $"The messaging provider did not respond to the {action} request in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsProviderException(
                $"The messaging provider could not be reached for the {action} request.", ex);
        }
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }
}
