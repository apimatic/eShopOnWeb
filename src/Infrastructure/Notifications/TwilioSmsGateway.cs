using System;
using System.Collections.Generic;
using System.Net;
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

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// The Twilio-backed <see cref="ISmsGateway"/>. Every messaging call is routed through the
/// AsadAli.TwilioSdk client; provider failures are translated to <see cref="SmsGatewayException"/>.
/// This class never writes a destination phone number or the auth token to logs.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    /// <summary>A whole-call budget for a single provider call (the only true call ceiling; the SDK's own timeouts are per-attempt).</summary>
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private const long ReconciliationPageSize = 100;
    private const int MaxReconciliationPages = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string SenderNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidationResult> ValidateDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var lookup = await ExecuteAsync(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, ct: ct), cancellationToken);

            if (lookup.Valid == true && !string.IsNullOrWhiteSpace(lookup.PhoneNumber))
                return PhoneNumberValidationResult.Valid(lookup.PhoneNumber!);

            return PhoneNumberValidationResult.Invalid();
        }
        catch (SmsGatewayException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            // The provider could not parse/resolve the number — it is not a usable destination.
            return PhoneNumberValidationResult.Invalid();
        }
    }

    public async Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        var message = await ExecuteAsync(ct => CreateMessageAsync(
            to: toNumber, body: body, scheduleType: null, sendAt: null,
            from: _settings.FromNumber, messagingServiceSid: null, ct: ct), cancellationToken);

        return ToDispatchResult(message);
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        // Scheduling must go through a Messaging Service; a plain From is not used for scheduled sends.
        var message = await ExecuteAsync(ct => CreateMessageAsync(
            to: toNumber, body: body, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
            from: null, messagingServiceSid: _settings.MessagingServiceSid, ct: ct), cancellationToken);

        return ToDispatchResult(message);
    }

    public async Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: providerMessageSid,
            body: null, status: MessageEnumUpdateStatus.Canceled, ct: ct), cancellationToken);
    }

    public async Task<string?> GetDeliveryStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        var message = await ExecuteAsync(ct => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid, sid: providerMessageSid, ct: ct), cancellationToken);

        return message.Status?.Value;
    }

    public async Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        // Updating the body to an empty string redacts the stored content at the provider while the
        // message record and its status survive.
        await ExecuteAsync(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: providerMessageSid,
            body: string.Empty, status: null, ct: ct), cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderMessage>();
        var page = 0;

        while (page < MaxReconciliationPages)
        {
            var currentPage = page;
            var response = await ExecuteAsync(ct => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,          // ask the provider only for this number's messages (server-side filter)
                dateSent: null,
                dateSentQuery: to,                    // DateSent< — upper bound (range "to")
                dateSentQueryQuery: from,             // DateSent> — lower bound (range "from")
                pageSize: ReconciliationPageSize,
                page: currentPage,
                pageToken: null,
                ct: ct), cancellationToken);

            var messages = response.Messages;
            if (messages is null || messages.Count == 0)
                break;

            foreach (var message in messages)
            {
                results.Add(new ProviderMessage
                {
                    Sid = message.Sid,
                    Status = message.Status?.Value,
                    From = message.From,
                    DateSent = message.DateSent
                });
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
                break;

            page++;
            if (page >= MaxReconciliationPages)
                _logger.LogWarning("Reconciliation stopped at the {0}-page cap; results may be incomplete.", MaxReconciliationPages);
        }

        return results;
    }

    // ---- SDK plumbing ---------------------------------------------------------------------------

    private Task<ApiV2010AccountMessage> CreateMessageAsync(
        string to, string body, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt,
        string? from, string? messagingServiceSid, CancellationToken ct)
    {
        // Every nullable-no-default parameter is passed explicitly by name so nothing mis-binds.
        return _client.Api20100401Message.CreateMessage(
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
            ct: ct);
    }

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
            throw new SmsGatewayException("The provider accepted the message but did not return a message identifier.");

        return new SmsDispatchResult { Sid = message.Sid!, Status = message.Status?.Value };
    }

    /// <summary>
    /// Runs a single provider call with a whole-call budget and translates every failure into an
    /// <see cref="SmsGatewayException"/> — API errors, unreadable responses, timeouts and transport failures.
    /// </summary>
    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw MapProviderError(ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model surfaces here, not as an SdkException.
            throw new SmsGatewayException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Our own budget elapsed (not the caller cancelling) — treat as a provider timeout.
            throw new SmsGatewayException("The messaging provider did not respond in time.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsGatewayException("The messaging provider is unreachable.", ex);
        }
    }

    private static SmsGatewayException MapProviderError(SdkException<RawError> ex)
    {
        var statusCode = ex.Error.StatusCode;
        int? providerCode = null;
        string? moreInfo = null;

        try
        {
            // Best-effort: read only the numeric error code and the documentation link. The provider's
            // free-text message can echo the destination number, so it is deliberately not extracted.
            var body = ex.Error.ReadAsString();
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("code", out var codeElement) &&
                        codeElement.ValueKind == JsonValueKind.Number &&
                        codeElement.TryGetInt32(out var code))
                    {
                        providerCode = code;
                    }

                    if (root.TryGetProperty("more_info", out var moreInfoElement) &&
                        moreInfoElement.ValueKind == JsonValueKind.String)
                    {
                        moreInfo = moreInfoElement.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Body was not JSON; fall back to the HTTP status alone.
        }

        return new SmsGatewayException(statusCode, providerCode, moreInfo);
    }
}
