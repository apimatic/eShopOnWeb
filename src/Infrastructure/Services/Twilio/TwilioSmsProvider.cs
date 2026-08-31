using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// The one place SDK calls happen. Translates every provider failure shape
/// (API rejection, transport failure, unreadable response) into
/// <see cref="SmsProviderException"/>; callers never see SDK types.
/// Phone numbers are never logged here.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    public const string HttpClientName = "Twilio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxReconciliationPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioOptions> options, ILogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(linked => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: linked), ct);

            var errors = new List<string>();
            if (response.ValidationErrors is not null)
            {
                foreach (var error in response.ValidationErrors)
                {
                    errors.Add(error.Value);
                }
            }

            var isValid = response.Valid == true;
            return new PhoneNumberValidationResult(isValid, isValid ? response.PhoneNumber : null, errors);
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "validate a phone number");
        }
    }

    public async Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(linked => _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
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
                from: _options.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: linked), ct);

            return new SmsSendResult(message.Sid ?? string.Empty, message.Status?.Value ?? NotificationStatusWire.Queued);
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "send a message");
        }
    }

    public async Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        try
        {
            // Provider-side scheduling is Messaging-Services-only: MessagingServiceSid
            // instead of From, with a fixed schedule type and the send-at instant.
            var message = await Bounded(linked => _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
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
                messagingServiceSid: _options.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: linked), ct);

            return new SmsSendResult(message.Sid ?? string.Empty, message.Status?.Value ?? NotificationStatusWire.Scheduled);
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "schedule a message");
        }
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            await Bounded(linked => _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: linked), ct);
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "cancel a scheduled message");
        }
    }

    public async Task<ProviderMessageState> GetMessageStateAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await Bounded(linked => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: providerMessageSid,
                requestOptions: null,
                ct: linked), ct);

            return new ProviderMessageState(
                message.Status?.Value ?? "unknown",
                message.ErrorCode,
                message.ErrorMessage,
                message.Body,
                ParseDate(message.DateSent));
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "fetch a message");
        }
    }

    public async Task RedactMessageBodyAsync(string providerMessageSid, CancellationToken ct = default)
    {
        try
        {
            // Redaction, not deletion: the record and its delivery outcome survive
            // at the provider with an empty body.
            await Bounded(linked => _client.Api20100401Message.UpdateMessage(
                accountSid: _options.AccountSid,
                sid: providerMessageSid,
                body: "",
                status: null,
                requestOptions: null,
                ct: linked), ct);
        }
        catch (Exception ex) when (ex is not SmsProviderException)
        {
            throw Translate(ex, "redact a message body");
        }
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var records = new List<ProviderMessageRecord>();
        string? pageToken = null;
        var page = 0;

        while (true)
        {
            ListMessagePage result;
            try
            {
                result = await Bounded(linked => ListPageAsync(from, to, page, pageToken, linked), ct);
            }
            catch (Exception ex) when (ex is not SmsProviderException)
            {
                throw Translate(ex, "list messages");
            }

            if (result.Messages is not null)
            {
                records.AddRange(result.Messages);
            }

            var nextToken = ExtractPageToken(result.NextPageUri);
            page++;

            // Termination: provider says no next page, token stops advancing, or the page cap.
            if (string.IsNullOrEmpty(result.NextPageUri) || nextToken is null || nextToken == pageToken)
            {
                break;
            }
            if (page >= MaxReconciliationPages)
            {
                _logger.LogWarning("Reconciliation listing hit the {MaxPages}-page cap; the report covers only the first {Count} provider records.",
                    MaxReconciliationPages, records.Count);
                break;
            }
            pageToken = nextToken;
        }

        return records;
    }

    private async Task<ListMessagePage> ListPageAsync(DateTimeOffset from, DateTimeOffset to, int page, string? pageToken, CancellationToken ct)
    {
        var response = await _client.Api20100401Message.ListMessage(
            accountSid: _options.AccountSid,
            to: null,
            from: _options.FromNumber,
            dateSent: null,
            dateSentQuery: to,
            dateSentQueryQuery: from,
            pageSize: 1000,
            page: page,
            pageToken: pageToken,
            requestOptions: null,
            ct: ct);

        var messages = new List<ProviderMessageRecord>();
        if (response.Messages is not null)
        {
            foreach (var m in response.Messages)
            {
                if (m.Sid is null) continue;
                messages.Add(new ProviderMessageRecord(
                    m.Sid,
                    m.From,
                    m.To,
                    m.Status?.Value ?? "unknown",
                    m.ErrorCode,
                    m.ErrorMessage,
                    ParseDate(m.DateSent),
                    ParseDate(m.DateCreated)));
            }
        }

        return new ListMessagePage(messages, response.NextPageUri);
    }

    private sealed record ListMessagePage(IReadOnlyList<ProviderMessageRecord> Messages, string? NextPageUri);

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri)) return null;
        var queryIndex = nextPageUri.IndexOf('?');
        if (queryIndex < 0) return null;
        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    /// <summary>
    /// The single catch ladder for the SDK boundary. API rejections keep their
    /// HTTP status; transport failures and unreadable provider bodies carry none.
    /// Messages stay caller-safe — no SDK type names, no request details.
    /// </summary>
    private SmsProviderException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdkEx:
                var detail = TryReadTwilioError(sdkEx.Error);
                _logger.LogWarning("Twilio rejected a request to {Operation} with status {StatusCode}: {Detail}",
                    operation, (int)sdkEx.Error.StatusCode, detail);
                return new SmsProviderException($"The messaging provider rejected the request to {operation}: {detail}",
                    sdkEx.Error.StatusCode, sdkEx);
            case JsonException:
                _logger.LogWarning("Twilio returned a response that could not be processed while trying to {Operation}.", operation);
                return new SmsProviderException("The messaging provider returned a response that could not be processed.", null, ex);
            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning("Twilio was unreachable while trying to {Operation}: {Error}", operation, ex.Message);
                return new SmsProviderException("The messaging provider could not be reached.", null, ex);
            default:
                _logger.LogError(ex, "Unexpected error while trying to {Operation}.", operation);
                return new SmsProviderException("Unexpected messaging provider error.", null, ex);
        }
    }

    private static string TryReadTwilioError(RawError raw)
    {
        try
        {
            var payload = raw.ReadAsJson<TwilioErrorPayload>();
            if (payload?.Message is not null)
            {
                return payload.Code is not null ? $"{payload.Message} (code {payload.Code})" : payload.Message;
            }
        }
        catch (JsonException)
        {
            // Body isn't Twilio's error JSON; fall through to the generic text.
        }
        return $"HTTP {(int)raw.StatusCode}";
    }

    private sealed class TwilioErrorPayload
    {
        [System.Text.Json.Serialization.JsonPropertyName("code")]
        public int? Code { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    private static class NotificationStatusWire
    {
        public const string Queued = "queued";
        public const string Scheduled = "scheduled";
    }
}


