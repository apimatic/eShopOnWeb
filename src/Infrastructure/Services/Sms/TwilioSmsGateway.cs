using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.Sms;

/// <summary>
/// The Twilio-backed <see cref="ISmsGateway"/>. All messaging goes through the Twilio SDK's
/// <c>Api20100401Message</c> controller; phone-number validation uses Lookup v2. Every operation is
/// Case B in the SDK's error model, so provider errors arrive as <see cref="SdkException{RawError}"/>;
/// transport failures as <see cref="HttpRequestException"/>/<see cref="TaskCanceledException"/>; and a
/// broken response body as <see cref="JsonException"/>. All three are handled at this boundary.
///
/// Nothing here logs the shopper's number, the message body, or the auth token — only message SIDs,
/// statuses, and numeric provider codes.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var lookup = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber, fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null, ct: ct);

            var isValid = lookup.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber);
            return new PhoneValidationResult(isValid, isValid ? lookup.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider could not parse/find the number — treat it as an unusable destination.
            return new PhoneValidationResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            var (_, reason) = DescribeError(ex);
            _logger.LogWarning($"Phone-number lookup failed: {reason}.");
            throw new SmsGatewayException("The phone number could not be validated with the provider.");
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Phone-number lookup returned an unreadable response.");
            throw new SmsGatewayException("The phone number could not be validated with the provider.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Phone-number lookup could not reach the provider.");
            throw new SmsGatewayException("The messaging provider is currently unavailable.", ex);
        }
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        try
        {
            var message = await CreateMessageAsync(toE164, body, scheduleType: null, sendAt: null,
                from: _settings.FromNumber, messagingServiceSid: null, ct);

            var status = message.Status?.Value ?? "queued";
            _logger.LogInformation($"SMS send accepted (sid {message.Sid}, status {status}).");
            return new SmsSendResult(true, message.Sid, status, message.ErrorCode, null);
        }
        catch (Exception ex) when (TryDescribeSendFailure(ex, "SMS send", out var result))
        {
            return result;
        }
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        try
        {
            // Scheduling requires a messaging service (scheduleType=Fixed is "for Messaging Services only").
            var message = await CreateMessageAsync(toE164, body,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
                from: null, messagingServiceSid: _settings.MessagingServiceSid, ct);

            var status = message.Status?.Value ?? "scheduled";
            _logger.LogInformation($"SMS follow-up scheduled (sid {message.Sid}, status {status}).");
            return new SmsSendResult(true, message.Sid, status, message.ErrorCode, null);
        }
        catch (Exception ex) when (TryDescribeSendFailure(ex, "SMS schedule", out var result))
        {
            return result;
        }
    }

    public async Task<SmsCancelResult> CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid, body: null,
                status: MessageEnumUpdateStatus.Canceled, ct: ct);

            _logger.LogInformation($"Scheduled SMS cancelled (sid {messageSid}, status {message.Status?.Value}).");
            return new SmsCancelResult(true, null);
        }
        catch (SdkException<RawError> ex)
        {
            // Any non-2xx here means the cancel did not take (already sent, past the window, ...).
            var (_, reason) = DescribeError(ex);
            _logger.LogWarning($"Scheduled SMS could not be cancelled (sid {messageSid}): {reason}.");
            return new SmsCancelResult(false, reason);
        }
        catch (JsonException)
        {
            return new SmsCancelResult(false, "Provider returned an unreadable response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new SmsCancelResult(false, "Provider unreachable.");
        }
    }

    public async Task<SmsDeliveryOutcome?> FetchStatusAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: messageSid, ct: ct);

            var status = message.Status?.Value;
            if (string.IsNullOrEmpty(status))
            {
                return null;
            }

            return new SmsDeliveryOutcome(status, message.ErrorCode);
        }
        catch (SdkException<RawError> ex)
        {
            var (_, reason) = DescribeError(ex);
            _logger.LogWarning($"Could not read delivery status (sid {messageSid}): {reason}.");
            return null;
        }
        catch (JsonException)
        {
            _logger.LogWarning($"Delivery-status read returned an unreadable response (sid {messageSid}).");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning($"Delivery-status read could not reach the provider (sid {messageSid}).");
            return null;
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = new List<ProviderMessageRecord>();
        const int maxPages = 1000; // backstop against a runaway loop

        try
        {
            for (var page = 0; page < maxPages; page++)
            {
                // Ask the provider for ONLY the configured From number's traffic in the range:
                //   dateSentQueryQuery -> wire "DateSent>" (lower bound = from)
                //   dateSentQuery      -> wire "DateSent<" (upper bound = to)
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid, to: null, from: _settings.FromNumber,
                    dateSent: null, dateSentQuery: to, dateSentQueryQuery: from,
                    pageSize: 50L, page: page, pageToken: null, ct: ct);

                var messages = response.Messages;
                if (messages is null || messages.Count == 0)
                {
                    break;
                }

                foreach (var message in messages)
                {
                    if (string.IsNullOrEmpty(message.Sid))
                    {
                        continue;
                    }

                    records.Add(new ProviderMessageRecord(
                        message.Sid!,
                        message.Status?.Value ?? "unknown",
                        message.From,
                        message.To,
                        message.ErrorCode,
                        ParseDate(message.DateSent)));
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }
            }

            return records;
        }
        catch (SdkException<RawError> ex)
        {
            var (_, reason) = DescribeError(ex);
            _logger.LogWarning($"Reconciliation listing failed: {reason}.");
            throw new SmsGatewayException("The messaging provider could not be queried for reconciliation.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned an unreadable reconciliation response.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is currently unavailable.", ex);
        }
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            // Redact the body at the provider by updating it to an empty string. This removes the text
            // while the message record (SID, status, outcome) survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid, body: string.Empty, status: null, ct: ct);

            _logger.LogInformation($"SMS content redacted at provider (sid {messageSid}).");
        }
        catch (SdkException<RawError> ex)
        {
            var (_, reason) = DescribeError(ex);
            _logger.LogWarning($"SMS content could not be redacted (sid {messageSid}): {reason}.");
            throw new SmsGatewayException("The message content could not be disposed of at the provider.");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned an unreadable response.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider is currently unavailable.", ex);
        }
    }

    // --- helpers -----------------------------------------------------------------------------

    private Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessageAsync(
        string toE164, string body, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
        string? from, string? messagingServiceSid, CancellationToken ct)
    {
        return _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid, to: toE164, statusCallback: null, applicationSid: null,
            maxPrice: null, provideFeedback: null, attempt: null, validityPeriod: null, forceDelivery: null,
            contentRetention: null, addressRetention: null, smartEncoded: null, persistentAction: null,
            trafficType: null, shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
            messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null, ct: ct);
    }

    /// <summary>
    /// Turns any provider/transport/parse failure of a send-style call into a non-throwing
    /// <see cref="SmsSendResult"/>, so a failed send never fails the underlying order operation.
    /// </summary>
    private bool TryDescribeSendFailure(Exception ex, string what, out SmsSendResult result)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkEx:
                var (code, reason) = DescribeError(sdkEx);
                _logger.LogWarning($"{what} rejected by provider: {reason}.");
                result = new SmsSendResult(false, null, "rejected", code, reason);
                return true;
            case JsonException:
                _logger.LogWarning($"{what}: provider returned an unreadable response.");
                result = new SmsSendResult(false, null, "rejected", null, "Provider returned an unreadable response.");
                return true;
            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning($"{what}: provider unreachable.");
                result = new SmsSendResult(false, null, "rejected", null, "Provider unreachable.");
                return true;
            default:
                result = default!;
                return false;
        }
    }

    /// <summary>
    /// Reads the HTTP status and numeric Twilio error code off a Case-B failure, without surfacing the
    /// provider's free-text message (which can embed the destination number). Returns only a safe reason.
    /// </summary>
    private static (int? Code, string Reason) DescribeError(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        int? code = null;
        try
        {
            var payload = ex.Error.ReadAsJson<TwilioErrorBody>();
            code = payload?.Code;
        }
        catch (JsonException)
        {
            // Error body was not JSON — keep the HTTP status only.
        }
        catch (Exception)
        {
            // Best-effort; never let error-reading throw.
        }

        var reason = code.HasValue
            ? $"provider returned HTTP {status} (code {code})"
            : $"provider returned HTTP {status}";
        return (code, reason);
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }
}
