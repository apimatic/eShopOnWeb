using System;
using System.Collections.Generic;
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
/// The Twilio messaging-API boundary. Every provider interaction goes through the Twilio .NET SDK client here,
/// and every failure — a rejected request, an unreachable host, an unreadable response — is translated into a
/// single <see cref="SmsGatewayException"/> so callers have one thing to handle.
///
/// Nothing in this class logs the destination number, the message body, provider error text, or any
/// credential.
/// </summary>
public class TwilioMessagingService : ITwilioMessagingService
{
    // Cap the reconciliation page walk so a provider that keeps handing out a next page can't spin forever.
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingService> _logger;

    public TwilioMessagingService(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
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
                ct: ct);

            if (response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber))
            {
                return PhoneNumberValidationResult.Valid(response.PhoneNumber);
            }

            return PhoneNumberValidationResult.Invalid("The provider does not consider this a usable destination.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            // The provider could not parse the number at all (a malformed number) — that is a rejection of the
            // caller's input, not an outage. Surface it as "invalid", not as a gateway error.
            if ((int)ex.Error.StatusCode == 404)
            {
                return PhoneNumberValidationResult.Invalid("The provider does not recognise this number.");
            }
            throw Translate(ex, "number lookup");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The provider returned an unreadable response for the number lookup.", null, null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The provider was unreachable for the number lookup.", null, null, ex);
        }
    }

    public Task<MessageDispatchResult> SendAsync(string toE164, string body, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var response = await _client.Api20100401Message.CreateMessage(
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
                ct: token);

            return ToDispatchResult(response, "send");
        }, "send", ct);

    public Task<MessageDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            // Scheduling is a Messaging-Service-only capability: send via the messaging service, not a bare From.
            var response = await _client.Api20100401Message.CreateMessage(
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
                ct: token);

            return ToDispatchResult(response, "schedule");
        }, "schedule", ct);

    public Task<MessageDispatchResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token);

            return ToDispatchResult(response, "cancel");
        }, "cancel", ct);

    public Task<MessageDispatchResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct = default) =>
        ExecuteAsync(async token =>
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: token);

            return ToDispatchResult(response, "fetch");
        }, "fetch", ct);

    public Task RedactContentAsync(string providerMessageSid, CancellationToken ct = default) =>
        ExecuteAsync<object?>(async token =>
        {
            // An empty body redacts the message text AT THE PROVIDER; the resource (and its outcome) survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: token);

            return null;
        }, "redact", ct);

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        ExecuteAsync<IReadOnlyList<ProviderMessageRecord>>(async token =>
        {
            var records = new List<ProviderMessageRecord>();
            int page = 0;

            while (true)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,               // server-side From filter (this app's number only)
                    dateSent: null,
                    dateSentQuery: to,                        // wire DateSent< : on/before range end
                    dateSentQueryQuery: from,                 // wire DateSent> : on/after range start
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: null,
                    ct: token);

                var messages = response.Messages;
                if (messages is not null)
                {
                    foreach (var m in messages)
                    {
                        if (string.IsNullOrEmpty(m.Sid))
                        {
                            continue;
                        }
                        records.Add(new ProviderMessageRecord(
                            sid: m.Sid!,
                            to: m.To,
                            from: m.From,
                            status: m.Status?.Value,
                            dateSent: ParseDate(m.DateSent)));
                    }
                }

                // Stop when the provider signals no further page, or nothing came back — and never spin past the cap.
                if (string.IsNullOrEmpty(response.NextPageUri) || messages is null || messages.Count == 0)
                {
                    break;
                }
                if (++page >= MaxReconciliationPages)
                {
                    _logger.LogWarning("Reconciliation reached the page cap ({MaxPages}); results may be truncated for this range.", MaxReconciliationPages);
                    break;
                }
            }

            return records;
        }, "list", ct);

    // ---- helpers ----

    private MessageDispatchResult ToDispatchResult(ApiV2010AccountMessage message, string operation)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new SmsGatewayException($"The provider accepted the {operation} but returned no message id.");
        }

        var status = message.Status?.Value ?? "unknown";
        return new MessageDispatchResult(message.Sid!, status, message.ErrorCode, message.ErrorMessage);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, string operationName, CancellationToken ct)
    {
        try
        {
            return await operation(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, operationName);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model: the outcome is genuinely unknown.
            throw new SmsGatewayException($"The provider returned an unreadable response for the {operationName}.", null, null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException($"The provider was unreachable during the {operationName}.", null, null, ex);
        }
    }

    private SmsGatewayException Translate(SdkException<RawError> ex, string operationName)
    {
        int? providerCode = null;
        string? providerMessage = null;

        // Best-effort read of Twilio's error body ({ "code": .., "message": .., "more_info": .., "status": .. }).
        try
        {
            var body = ex.Error.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number && codeEl.TryGetInt32(out var code))
                    {
                        providerCode = code;
                    }
                    if (root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
                    {
                        providerMessage = msgEl.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Body wasn't JSON — fall back to a generic message.
        }

        // Log status and provider code only — never the message text (it can reference the destination number).
        _logger.LogWarning("Twilio {Operation} failed with HTTP {Status} (provider code {Code}).", operationName, (int)ex.Error.StatusCode, providerCode);

        var message = providerMessage ?? $"The provider rejected the {operationName}.";
        return new SmsGatewayException(message, ex.Error.StatusCode, providerCode, ex);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
}
