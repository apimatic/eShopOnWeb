using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The <see cref="ISmsGateway"/> implementation backed by the Twilio .NET SDK. All provider outcomes are
/// translated into provider-neutral results here so the application layer never depends on the SDK.
/// Sending operations swallow-and-record failures (never throw); read/validate/reconcile/redact operations
/// translate provider errors into <see cref="SmsGatewayException"/>. Shopper numbers are never logged.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, TwilioSettings settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: ct);

            var valid = response.Valid ?? false;
            if (!valid || string.IsNullOrEmpty(response.PhoneNumber))
                return new PhoneValidationResult(false, null, "The number is not a valid, reachable SMS destination.");

            return new PhoneValidationResult(true, response.PhoneNumber, null);
        }
        catch (SdkException<RawError> ex)
        {
            // A malformed or non-existent number is a rejection, not an outage — surface it as "invalid".
            var status = (int)ex.Error.StatusCode;
            if (status is 400 or 404)
                return new PhoneValidationResult(false, null, "The number is not a valid, reachable SMS destination.");
            throw ToGatewayException("validate the phone number", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The phone-number validation service could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The phone-number validation service returned a response that could not be processed.", ex);
        }
    }

    public async Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct = default)
        => await CreateMessageSafeAsync(toNumber, body, from: _settings.FromNumber, messagingServiceSid: null,
            scheduleType: null, sendAt: null, ct);

    public async Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
        // Scheduling requires a messaging service; leave 'from' unset so the service supplies the sender.
        => await CreateMessageSafeAsync(toNumber, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, ct);

    private async Task<SmsDispatchResult> CreateMessageSafeAsync(string toNumber, string body, string? from,
        string? messagingServiceSid, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, CancellationToken ct)
    {
        try
        {
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: scheduleType,
                sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: from,
                fallbackFrom: null,
                messagingServiceSid: messagingServiceSid,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: ct);

            if (string.IsNullOrEmpty(response.Sid))
            {
                _logger.LogWarning("Provider accepted a message but returned no SID.");
                return SmsDispatchResult.Failure(response.ErrorCode, response.ErrorMessage ?? "No message identifier was returned.");
            }

            return SmsDispatchResult.Success(response.Sid!, response.Status?.Value);
        }
        catch (SdkException<RawError> ex)
        {
            var (code, message) = ReadProviderError(ex);
            _logger.LogWarning("Provider rejected an outbound message (HTTP {Status}).", (int)ex.Error.StatusCode);
            return SmsDispatchResult.Failure(code, message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Outbound message could not be delivered to the provider (transport failure).");
            return SmsDispatchResult.Failure(null, "The messaging provider could not be reached.");
        }
        catch (JsonException)
        {
            // A 2xx body we could not read: the outcome is unknown — record it as a failure to send.
            _logger.LogWarning("Provider returned an unreadable response for an outbound message.");
            return SmsDispatchResult.Failure(null, "The provider returned a response that could not be processed.");
        }
    }

    public async Task<bool> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not cancel scheduled message {Sid}: {Reason}.", providerMessageSid, ex.Message);
            return false;
        }
    }

    public async Task<MessageStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: ct);

            return new MessageStatusResult(response.Status?.Value, response.ErrorCode, response.ErrorMessage);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("read the message status", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
    }

    public async Task RedactBodyAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            // Setting the body to an empty string redacts the stored text at the provider while keeping the record.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw ToGatewayException("dispose the message content", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The messaging provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        const int maxPages = 1000; // provider-independent backstop against an unbounded page loop
        var page = 0;

        do
        {
            ListMessageResponse response;
            try
            {
                response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,          // server-side filter: only this application's sending number
                    dateSent: null,
                    dateSentQuery: to,                    // DateSent<= (on/before) upper bound
                    dateSentQueryQuery: from,             // DateSent>= (on/after) lower bound
                    pageSize: 200,
                    page: null,
                    pageToken: pageToken,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw ToGatewayException("list provider messages", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new SmsGatewayException("The messaging provider could not be reached.", ex);
            }
            catch (JsonException ex)
            {
                throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
            }

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    if (string.IsNullOrEmpty(message.Sid))
                        continue;
                    results.Add(new ProviderMessageRecord(
                        Sid: message.Sid!,
                        Status: message.Status?.Value,
                        DateSent: ParseProviderDate(message.DateSent),
                        To: null,
                        From: _settings.FromNumber));
                }
            }

            pageToken = ExtractPageToken(response.NextPageUri);
            page++;
        }
        while (pageToken is not null && page < maxPages);

        if (page >= maxPages && pageToken is not null)
            _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page cap; results may be incomplete.", maxPages);

        return results;
    }

    // ---------------- helpers ----------------

    private SmsGatewayException ToGatewayException(string action, SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        // Deliberately do not embed the provider's error text (it may echo a destination number) in the message.
        return new SmsGatewayException($"The messaging provider could not {action} (HTTP {status}).", ex)
        {
            ProviderStatusCode = status
        };
    }

    /// <summary>
    /// Reads the provider's error code/message defensively: attempt JSON, fall back to the raw string, and
    /// never let the extraction itself throw.
    /// </summary>
    private static (int? code, string? message) ReadProviderError(SdkException<RawError> ex)
    {
        try
        {
            var body = ex.Error.ReadAsJson<TwilioErrorBody>();
            if (body is not null && (body.Code.HasValue || !string.IsNullOrEmpty(body.Message)))
                return (body.Code, Truncate(body.Message));
        }
        catch (Exception)
        {
            // fall through to the raw string
        }

        try
        {
            var raw = ex.Error.ReadAsString();
            return (null, string.IsNullOrWhiteSpace(raw) ? null : Truncate(raw));
        }
        catch (Exception)
        {
            return (null, null);
        }
    }

    private static string? Truncate(string? value) =>
        value is null ? null : (value.Length <= 500 ? value : value.Substring(0, 500));

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
            return null;

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
            return null;

        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                continue;
            var name = Uri.UnescapeDataString(pair.Substring(0, eq));
            if (!string.Equals(name, "PageToken", StringComparison.Ordinal))
                continue;
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return null;
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
