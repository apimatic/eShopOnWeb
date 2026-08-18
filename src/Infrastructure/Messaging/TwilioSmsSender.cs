using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsSender"/>. Every messaging-API operation goes
/// through <see cref="InvokeAsync{T}"/>, which puts a whole-call deadline on the request and translates
/// every provider/transport failure into a single <see cref="SmsProviderException"/>. Provider response
/// bodies (which can echo the destination number) are never surfaced or logged.
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsSender(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        try
        {
            // Lookup lives on a different host than the messaging API and is not governed by Twilio:BaseUrl.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: cts.Token);

            var errors = response.ValidationErrors?.Select(e => e.Value).ToList() ?? new List<string>();
            return new PhoneValidationResult(response.Valid == true, response.PhoneNumber, errors);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // Lookup returns 404 for a number it cannot resolve — that is an "unusable destination"
            // verdict, not a provider outage.
            return new PhoneValidationResult(false, null, new[] { "not_found" });
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            throw new SmsProviderException("The messaging provider is unreachable or timed out.", null, ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
                riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);
            return ToSendResult(message);
        }, cancellationToken);

    public Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            // A scheduled ("fixed") send must go through a Messaging Service and carries no explicit From.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
                contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);
            return ToSendResult(message);
        }, cancellationToken);

    public Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: ct);
            return true;
        }, cancellationToken);

    public Task<MessageDeliveryInfo> FetchAsync(string messageSid, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: messageSid, ct: ct);
            return new MessageDeliveryInfo(message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }, cancellationToken);

    public Task RedactContentAsync(string messageSid, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            // Redact the body at the provider by updating it to empty; the record and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid, sid: messageSid,
                body: string.Empty, status: null, ct: ct);
            return true;
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderMessage>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
        => InvokeAsync(async ct =>
        {
            var results = new List<ProviderMessage>();
            int? page = null;
            string? pageToken = null;

            // Manual paging (the SDK returns one page per call). A page cap bounds the loop regardless of
            // what the provider reports, so the report can never spin unbounded.
            for (var i = 0; i < MaxReconciliationPages; i++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,        // ask only for this application's own number
                    dateSent: null,
                    dateSentQuery: to,                 // DateSent<  (upper bound)
                    dateSentQueryQuery: from,          // DateSent>  (lower bound)
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: ct);

                if (response.Messages is { Count: > 0 })
                {
                    results.AddRange(response.Messages.Select(ToProviderMessage));
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }

                (page, pageToken) = ParseNextPage(response.NextPageUri);
                if (pageToken is null)
                {
                    break; // cannot advance safely; stop rather than refetch the same page forever
                }
            }

            return (IReadOnlyList<ProviderMessage>)results;
        }, cancellationToken);

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message)
    {
        var sid = message.Sid
            ?? throw new SmsProviderException("The messaging provider accepted the message but returned no identifier.");
        return new SmsSendResult(sid, message.Status?.Value);
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = DateTimeOffset.TryParse(message.DateSent, out var parsed) ? parsed : null;
        return new ProviderMessage(message.Sid ?? string.Empty, message.Status?.Value, message.From, dateSent, message.ErrorCode);
    }

    /// <summary>
    /// Runs a messaging-API call under a whole-call deadline and a single failure boundary. Every
    /// provider error becomes an <see cref="SmsProviderException"/> carrying the HTTP status but never the
    /// response body.
    /// </summary>
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds));
        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            // A 2xx body that no longer matches the model: outcome genuinely unknown.
            throw new SmsProviderException("The messaging provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw; // caller cancelled — propagate cancellation
            throw new SmsProviderException("The messaging provider is unreachable or timed out.", null, ex);
        }
    }

    private static SmsProviderException Translate(SdkException<RawError> ex)
        => new($"The messaging provider returned an error (HTTP {(int)ex.Error.StatusCode}).", ex.Error.StatusCode, ex);

    /// <summary>Extracts the <c>Page</c> and <c>PageToken</c> query values from a next-page URI.</summary>
    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return (null, null);
        }

        int? page = null;
        string? pageToken = null;
        foreach (var pair in nextPageUri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0) continue;
            var name = pair[..eq];
            var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
            if (name.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (name.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }
}
