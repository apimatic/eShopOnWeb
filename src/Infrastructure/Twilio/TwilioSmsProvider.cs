using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The single boundary over the Twilio .NET SDK. Every SDK failure — an API error
/// (<see cref="SdkException{RawError}"/>), a transport failure, or an unreadable success body
/// (<see cref="JsonException"/>) — is translated into <see cref="SmsProviderException"/> with a
/// caller-safe message that never contains a contact number.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    // Reconciliation backstop: never page beyond this, whatever the provider keeps returning.
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsProvider> _logger;

    public TwilioSmsProvider(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsProvider> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

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

            bool usable = response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber);
            return new PhoneValidationResult(usable, usable ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex)
        {
            // A number the provider can't resolve surfaces as a 4xx (commonly 404); that is "not a
            // usable destination", not an outage. A 5xx means we genuinely couldn't consult the provider.
            int status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500)
            {
                return new PhoneValidationResult(false, null);
            }
            throw Translate("phone lookup", ex);
        }
        catch (JsonException ex)
        {
            // Never treat an unreadable response as a definite "invalid" — surface it instead.
            throw Translate("phone lookup", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Translate("phone lookup", ex);
        }
    }

    public Task<SentMessageResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        return ExecuteAsync("send message", async () =>
        {
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: ct);

            return new SentMessageResult(
                response.Sid,
                response.Status?.Value ?? "unknown",
                response.ErrorCode,
                response.ErrorMessage);
        });
    }

    public Task<SentMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        return ExecuteAsync("schedule message", async () =>
        {
            // Scheduling requires a Messaging Service (not a plain From number) plus a fixed send time.
            var response = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toE164,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: null,
                fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null, contentSid: null,
                ct: ct);

            return new SentMessageResult(
                response.Sid,
                response.Status?.Value ?? "unknown",
                response.ErrorCode,
                response.ErrorMessage);
        });
    }

    public Task<MessageDeliveryState> CancelScheduledAsync(string providerSid, CancellationToken ct = default)
    {
        return ExecuteAsync("cancel scheduled message", async () =>
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);

            return ToDeliveryState(providerSid, response);
        });
    }

    public Task<MessageDeliveryState> GetMessageStateAsync(string providerSid, CancellationToken ct = default)
    {
        return ExecuteAsync("fetch message", async () =>
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: ct);

            return ToDeliveryState(providerSid, response);
        });
    }

    public Task RedactContentAsync(string providerSid, CancellationToken ct = default)
    {
        return ExecuteAsync("redact message", async () =>
        {
            // Empties the body text at the provider while the message record + status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: string.Empty,
                status: null,
                ct: ct);
            return true;
        });
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessageRecord>();
        var seenSids = new HashSet<string>(StringComparer.Ordinal);
        int page = 0;

        while (page < MaxReconciliationPages)
        {
            int pageIndex = page;
            var response = await ExecuteAsync("list messages", async () =>
                // from -> DateSent> (on/after start); to -> DateSent< (on/before end). Filtered by the
                // configured From number at the provider, not client-side.
                await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ReconciliationPageSize,
                    page: pageIndex,
                    pageToken: null,
                    ct: ct));

            var messages = response.Messages;
            if (messages is null || messages.Count == 0)
            {
                break;
            }

            int newThisPage = 0;
            foreach (var message in messages)
            {
                var sid = message.Sid ?? string.Empty;
                if (sid.Length == 0 || !seenSids.Add(sid))
                {
                    continue; // guard against a provider that does not advance the page
                }
                newThisPage++;
                results.Add(new ProviderMessageRecord(
                    sid,
                    message.To,
                    message.From,
                    message.Status?.Value ?? "unknown",
                    message.DateSent));
            }

            // Stop conditions that don't rely on the provider's cooperation alone.
            if (newThisPage == 0) break;
            if (messages.Count < ReconciliationPageSize) break;
            if (string.IsNullOrEmpty(response.NextPageUri)) break;

            page++;
        }

        if (page >= MaxReconciliationPages)
        {
            _logger.LogWarning("Reconciliation stopped at the {MaxPages}-page cap; results may be truncated.", MaxReconciliationPages);
        }

        // Confirms the provider applied the From filter server-side: every returned message should be
        // from the configured sending number. Logs only a count and the (non-PII) mismatch total.
        int fromMismatches = results.Count(r => !string.Equals(r.From, _settings.FromNumber, StringComparison.Ordinal));
        _logger.LogInformation("Reconciliation listed {Total} provider message(s) from the configured number; {Mismatch} with a different From.",
            results.Count, fromMismatches);

        return results;
    }

    private static MessageDeliveryState ToDeliveryState(string providerSid, global::TwilioSdk.Models.ApiV2010AccountMessage response)
    {
        return new MessageDeliveryState(
            response.Sid ?? providerSid,
            response.Status?.Value ?? "unknown",
            response.ErrorCode,
            response.ErrorMessage,
            response.DateSent);
    }

    private static async Task<T> ExecuteAsync<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(operation, ex);
        }
        catch (JsonException ex)
        {
            throw Translate(operation, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Translate(operation, ex);
        }
    }

    private static SmsProviderException Translate(string operation, Exception exception)
    {
        switch (exception)
        {
            case SdkException<RawError> sdk:
                int status = (int)sdk.Error.StatusCode;
                bool deterministic = status is >= 400 and < 500;
                return new SmsProviderException($"Twilio {operation} failed (status {status}).", status, deterministic, sdk);
            case JsonException:
                return new SmsProviderException($"Twilio {operation} returned an unreadable response.", null, false, exception);
            default:
                return new SmsProviderException($"Twilio {operation} could not reach the provider.", null, false, exception);
        }
    }
}
