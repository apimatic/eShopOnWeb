using System;
using System.Collections.Generic;
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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The only place this application talks to Twilio. Translates the SDK's contract into the domain's
/// <see cref="ISmsProvider"/> shape: a provider-answered result (including a carrier-refused message) comes
/// back as a value; a transport/config/parse failure — no provider state — is raised as
/// <see cref="SmsProviderException"/>.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // The provider's send-time listing pages; keep a hard bound so a misbehaving page cursor can never spin.
    private const long ListPageSize = 1000;
    private const int MaxReconciliationPages = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            // Lookup uses its own host; the messaging BaseUrl override does not apply here.
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

            var isValid = response.Valid ?? false;
            return new PhoneValidationResult(isValid, isValid ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when ((int)ex.Error.StatusCode == 404)
        {
            // The provider does not recognise this as a usable/known destination — treat as invalid, not an outage.
            return new PhoneValidationResult(false, null);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "phone-number lookup", cancellationToken);
        }
    }

    public Task<SmsDispatchResult> SendAsync(string toPhoneNumber, string body, CancellationToken cancellationToken = default)
        => CreateMessageAsync(toPhoneNumber, body, scheduled: false, sendAt: null, cancellationToken);

    public Task<SmsDispatchResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => CreateMessageAsync(toPhoneNumber, body, scheduled: true, sendAt: sendAt, cancellationToken);

    private async Task<SmsDispatchResult> CreateMessageAsync(string toPhoneNumber, string body, bool scheduled, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        try
        {
            // Scheduling is Messaging-Services-only: set scheduleType + sendAt + messagingServiceSid, no From.
            // An immediate send is attributed to the configured From number (so reconciliation can find it).
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
                scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                sendAt: scheduled ? sendAt : null,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: scheduled ? null : _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: cancellationToken);

            _logger.LogInformation("Twilio {0} accepted; sid {1}, status {2}.",
                scheduled ? "schedule" : "send", message.Sid, message.Status?.Value);
            return ToDispatchResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, scheduled ? "scheduled send" : "send", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> GetStatusAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: cancellationToken);
            return ToDispatchResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "message read", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken);
            _logger.LogInformation("Twilio cancel accepted for sid {0}; status {1}.", message.Sid, message.Status?.Value);
            return ToDispatchResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "message cancel", cancellationToken);
        }
    }

    public async Task<SmsDispatchResult> RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
    {
        try
        {
            // Empty body redacts the provider-side content while the message record (sid, status, dates) survives.
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: cancellationToken);
            _logger.LogInformation("Twilio content disposal accepted for sid {0}.", message.Sid);
            return ToDispatchResult(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "content disposal", cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var records = new List<ProviderMessageRecord>();
        string? pageToken = null;
        var pages = 0;

        try
        {
            do
            {
                // Ask the provider for THIS application's own sending number's messages in the range, rather
                // than filtering a wider answer afterwards. dateSentQueryQuery = on/after (lower bound),
                // dateSentQuery = on/before (upper bound).
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: cancellationToken);

                if (response.Messages is not null)
                {
                    foreach (var message in response.Messages)
                    {
                        records.Add(new ProviderMessageRecord
                        {
                            Sid = message.Sid,
                            Status = message.Status?.Value,
                            To = message.To,
                            From = message.From,
                            DateSent = message.DateSent,
                            ErrorCode = message.ErrorCode,
                            ErrorMessage = message.ErrorMessage
                        });
                    }
                }

                pageToken = ExtractPageToken(response.NextPageUri);
            }
            while (!string.IsNullOrEmpty(pageToken) && ++pages < MaxReconciliationPages);

            if (pages >= MaxReconciliationPages && !string.IsNullOrEmpty(pageToken))
            {
                _logger.LogWarning("Reconciliation stopped at the {0}-page cap; the range may be larger than reported.", MaxReconciliationPages);
            }

            return records;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "message list", cancellationToken);
        }
    }

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message) => new()
    {
        MessageSid = message.Sid,
        Status = message.Status?.Value,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage,
        Body = message.Body
    };

    /// <summary>Pull the <c>PageToken</c> value out of the provider's next-page URI, if any.</summary>
    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        const string marker = "PageToken=";
        var start = nextPageUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = nextPageUri.IndexOf('&', start);
        var raw = end < 0 ? nextPageUri[start..] : nextPageUri[start..end];
        return Uri.UnescapeDataString(raw);
    }

    /// <summary>
    /// Turn any exception from an SDK call into a domain <see cref="SmsProviderException"/>. A provider error
    /// status is carried through; a JSON/transport failure carries no status. Genuine cancellation of the
    /// caller's own token is left to propagate.
    /// </summary>
    private static SmsProviderException Translate(Exception ex, string operation, CancellationToken cancellationToken)
    {
        switch (ex)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                throw ex; // the caller cancelled; not a provider fault
            case SdkException<RawError> sdk:
                return new SmsProviderException($"Twilio {operation} failed (HTTP {(int)sdk.Error.StatusCode}).", (int)sdk.Error.StatusCode, sdk);
            case JsonException:
                return new SmsProviderException($"Twilio {operation} returned a response that could not be processed.", null, ex);
            case HttpRequestException:
            case TaskCanceledException:
            case OperationCanceledException:
                return new SmsProviderException($"Twilio {operation} could not reach the provider.", null, ex);
            default:
                return new SmsProviderException($"Twilio {operation} failed.", null, ex);
        }
    }
}
