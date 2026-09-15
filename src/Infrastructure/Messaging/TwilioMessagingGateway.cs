using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Talks to Twilio through the APIMatic-generated .NET SDK — the sole place SDK types are used. Sends,
/// reads, cancels, redacts and reconciles messages, and validates numbers via Lookup. Every call is bounded,
/// and every provider/transport failure is translated to <see cref="TwilioMessagingException"/>.
/// Secrets and phone numbers are never logged.
/// </summary>
public class TwilioMessagingGateway : ITwilioMessagingGateway
{
    /// <summary>Whole-call budget (covers retries + backoff); the SDK's per-attempt timeout is set separately.</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 200L;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            // Lookup lives on the lookups host — unaffected by Twilio:BaseUrl (which overrides messaging only).
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
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
                ct: cts.Token);

            var valid = response.Valid == true && !string.IsNullOrEmpty(response.PhoneNumber);
            return new PhoneValidationResult(
                valid,
                valid ? response.PhoneNumber : null,
                valid ? null : "The number is not a valid SMS destination.");
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup can answer "not found" for an unresolvable number — that is a rejection, not an outage.
            return new PhoneValidationResult(false, null, "The number could not be found or is not a valid destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw new TwilioMessagingException("The messaging provider could not validate the number.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new TwilioMessagingException("The messaging provider returned a response that could not be processed while validating the number.", null, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new TwilioMessagingException("The messaging provider was unreachable while validating the number.", null, ex);
        }
    }

    public Task<ProviderMessageState> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken) =>
        ExecuteAsync(async token =>
        {
            var message = await CreateMessageAsync(toE164, body, from: _settings.FromNumber,
                messagingServiceSid: null, scheduleType: null, sendAt: null, token);
            return ToState(message);
        }, "send", cancellationToken);

    public Task<ProviderMessageState> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        ExecuteAsync(async token =>
        {
            // Scheduling is a Messaging-Service capability: no From number, a Fixed schedule type and a send-at time.
            var message = await CreateMessageAsync(toE164, body, from: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, token);
            return ToState(message);
        }, "schedule", cancellationToken);

    public Task<ProviderMessageState> CancelScheduledMessageAsync(string messageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async token =>
        {
            var message = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: token);
            return ToState(message);
        }, "cancel", cancellationToken);

    public Task<ProviderMessageState> GetMessageStateAsync(string messageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async token =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                ct: token);
            return ToState(message);
        }, "fetch", cancellationToken);

    public Task RedactMessageBodyAsync(string messageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async token =>
        {
            // An empty body redacts the stored text at the provider while preserving the record.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: token);
            return true;
        }, "redact", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessageSummary>> ListMessagesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<ProviderMessageSummary>();
        int? page = 0;
        string? pageToken = null;

        for (var pageIndex = 0; pageIndex < MaxReconciliationPages; pageIndex++)
        {
            var currentPage = page;
            var currentToken = pageToken;

            var response = await ExecuteAsync(token => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,              // ask the provider for THIS number's messages
                dateSent: null,
                dateSentQuery: to,                        // DateSent< : sent before the range end
                dateSentQueryQuery: from,                 // DateSent> : sent after the range start
                pageSize: ReconciliationPageSize,
                page: currentPage,
                pageToken: currentToken,
                ct: token), "list-messages", cancellationToken);

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    results.Add(new ProviderMessageSummary(
                        message.Sid, message.Status?.Value, message.From, message.To, message.DateSent));
                }
            }

            if (string.IsNullOrEmpty(response.NextPageUri) || response.Messages is null || response.Messages.Count == 0)
            {
                break;
            }

            var (nextPage, nextToken) = ParseNextPage(response.NextPageUri);
            if (nextPage is null && nextToken is null)
            {
                break; // cannot advance safely — stop rather than loop forever
            }

            page = nextPage ?? page + 1;
            pageToken = nextToken;
        }

        return results;
    }

    private async Task<ApiV2010AccountMessage> CreateMessageAsync(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        // Named arguments throughout: every optional parameter is nullable-with-no-default and must be passed.
        return await _client.Api20100401Message.CreateMessage(
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
            scheduleType: scheduleType,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: from,
            fallbackFrom: null,
            messagingServiceSid: messagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            ct: cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, string action, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await operation(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            // The provider returned a non-2xx. Carry the status so the boundary can tell a caller-fixable
            // 4xx from a provider 5xx.
            throw new TwilioMessagingException($"The messaging provider rejected the {action} request.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            // A drifted/mismatched body: the status is lost with it. Treat as a provider processing error,
            // not an outage.
            throw new TwilioMessagingException($"The messaging provider returned a response that could not be processed for the {action} request.", null, ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller cancelled — propagate cancellation
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new TwilioMessagingException($"The messaging provider was unreachable for the {action} request.", null, ex);
        }
    }

    private static ProviderMessageState ToState(ApiV2010AccountMessage message) =>
        new(message.Sid ?? string.Empty, message.Status?.Value ?? "unknown", message.ErrorCode, message.ErrorMessage);

    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;

        foreach (var pair in nextPageUri[(queryStart + 1)..].Split('&'))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(kv[0]);
            var value = Uri.UnescapeDataString(kv[1]);

            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var parsedPage))
            {
                page = parsedPage;
            }
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }
}
