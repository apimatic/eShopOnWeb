using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// ISmsService over the Twilio .NET SDK. Sends report failure as an outcome (never throw),
/// so a message that cannot go out never fails the underlying order operation. Validation
/// and listing throw SmsProviderException on provider faults. Phone numbers and message
/// bodies are never logged.
/// </summary>
public class TwilioSmsService : ISmsService
{
    public const string HttpClientName = "Twilio";

    // One deadline for a whole provider interaction, not per attempt.
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50; // hard bound so a misbehaving provider cannot spin the loop

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioSmsService> _logger;

    public TwilioSmsService(TwilioSdkClient client, IOptions<TwilioSettings> settings, ILogger<TwilioSmsService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: token), ct);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber)
                && (response.ValidationErrors is null || response.ValidationErrors.Count == 0))
            {
                return new PhoneNumberValidationResult(true, response.PhoneNumber, null);
            }

            var reasons = response.ValidationErrors is null
                ? "not a valid number"
                : string.Join(", ", response.ValidationErrors);
            return new PhoneNumberValidationResult(false, null, reasons);
        }
        catch (SdkException<RawError> ex)
        {
            // An unparseable number may be rejected outright rather than returned as Valid=false.
            if ((int)ex.Error.StatusCode is >= 400 and < 500)
            {
                return new PhoneNumberValidationResult(false, null, ReadErrorBody(ex.Error));
            }
            throw ToProviderException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The SMS provider could not be reached.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string to, string body, CancellationToken ct = default) =>
        SendGuarded(token => _client.Api20100401Message.CreateMessage(
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
            requestOptions: null,
            ct: token), ct);

    public Task<SmsSendResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        SendGuarded(token => _client.Api20100401Message.CreateMessage(
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
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: null, // scheduled sends go through the messaging service, not a bare From number
            fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: token), ct);

    public Task<SmsSendResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default) =>
        SendGuarded(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: token), ct);

    public Task<SmsSendResult> RedactBodyAsync(string messageSid, CancellationToken ct = default) =>
        SendGuarded(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            body: string.Empty,
            status: null,
            requestOptions: null,
            ct: token), ct);

    public Task<SmsSendResult> FetchAsync(string messageSid, CancellationToken ct = default) =>
        SendGuarded(token => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: messageSid,
            requestOptions: null,
            ct: token), ct);

    public async Task<IReadOnlyList<ProviderSmsMessage>> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var messages = new List<ProviderSmsMessage>();
        try
        {
            string? nextPageUri;
            var page = 0;
            do
            {
                var response = await Bounded(token => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber, // ask the provider for this app's own traffic only
                    dateSent: null,
                    dateSentQuery: to,          // wire name: DateSent<
                    dateSentQueryQuery: from,   // wire name: DateSent>
                    pageSize: 100,
                    page: page,
                    pageToken: null,
                    requestOptions: null,
                    ct: token), ct);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        if (m.Sid is null)
                        {
                            continue;
                        }
                        messages.Add(new ProviderSmsMessage(
                            m.Sid,
                            m.To,
                            m.From,
                            m.Status?.Value,
                            ParseProviderDate(m.DateSent),
                            m.ErrorCode,
                            m.ErrorMessage));
                    }
                }

                nextPageUri = response.NextPageUri;
                page++;
            }
            while (nextPageUri is not null && page < MaxListPages);

            if (nextPageUri is not null)
            {
                _logger.LogWarning("Reconciliation listing hit the page cap of {maxPages}; the report may be incomplete.", MaxListPages);
            }

            return messages;
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The SMS provider could not be reached.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The SMS provider returned a response that could not be processed.", null, ex);
        }
    }

    // Sends report failure as an outcome: the caller records it on the notification
    // and the underlying operation still succeeds.
    private async Task<SmsSendResult> SendGuarded(
        Func<CancellationToken, Task<TwilioSdk.Models.ApiV2010AccountMessage>> call, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(call, ct);
            if (message.Sid is null)
            {
                return SmsSendResult.Failed(message.Status?.Value, message.ErrorCode,
                    "The provider accepted the request but returned no message identifier.");
            }
            return SmsSendResult.Accepted(message.Sid, message.Status?.Value);
        }
        catch (SdkException<RawError> ex)
        {
            return SmsSendResult.Failed(null, (int)ex.Error.StatusCode, ReadErrorBody(ex.Error));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // A transport failure on a write means the send may already have taken effect;
            // the reconciliation report is how such a message is found again.
            return SmsSendResult.Failed(null, null, "The SMS provider could not be reached; the send outcome is unknown.");
        }
        catch (JsonException)
        {
            return SmsSendResult.Failed(null, null, "The SMS provider returned a response that could not be processed.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsProviderException ToProviderException(SdkException<RawError> ex) =>
        new("The SMS provider rejected the request.", ex.Error.StatusCode, ex);

    private string ReadErrorBody(RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            if (body?.Message is not null)
            {
                return body.Message;
            }
        }
        catch (JsonException)
        {
            // fall through to the raw body
        }
        return error.ReadAsString();
    }

    private static DateTimeOffset? ParseProviderDate(string? dateSent) =>
        DateTimeOffset.TryParse(dateSent, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    // Twilio error bodies carry code/message/more_info/status.
    private sealed record TwilioErrorBody(
        [property: System.Text.Json.Serialization.JsonPropertyName("code")] int? Code,
        [property: System.Text.Json.Serialization.JsonPropertyName("message")] string? Message,
        [property: System.Text.Json.Serialization.JsonPropertyName("more_info")] string? MoreInfo,
        [property: System.Text.Json.Serialization.JsonPropertyName("status")] int? Status);
}
