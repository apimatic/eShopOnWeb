using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsProvider"/>. All Twilio SDK contact is confined here; the
/// rest of the app sees only the neutral <see cref="ISmsProvider"/> shapes. Every call is bounded by a single
/// whole-call deadline and every provider/transport failure is translated into a single
/// <see cref="SmsProviderException"/> that carries the provider's HTTP status (when one was returned). No phone
/// number is ever placed into a log or an exception message.
/// </summary>
public class TwilioSmsProvider : ISmsProvider
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly TimeSpan _budget;

    public TwilioSmsProvider(TwilioSdkClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
        _budget = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
    }

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken cancellationToken)
    {
        LookupResponse response;
        try
        {
            response = await InvokeAsync(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: ct), cancellationToken);
        }
        catch (SmsProviderException ex) when (IsClientError(ex.StatusCode))
        {
            // The provider rejected the number itself (e.g. un-parseable) — that is "not a usable destination"
            // at registration time, not an outage. A 5xx / transport failure is NOT caught here and propagates.
            return new PhoneValidationResult(false, null, new[] { $"The number is not a usable destination (provider status {(int)ex.StatusCode!.Value})." });
        }

        // Treat a null Valid as "not usable" — do not assume usability the provider did not assert.
        var isUsable = response.Valid == true;
        var canonical = response.PhoneNumber; // provider's canonical E.164 form — this is what we store.

        if (!isUsable || string.IsNullOrEmpty(canonical))
        {
            return new PhoneValidationResult(false, canonical, MapReasons(response.ValidationErrors));
        }

        return new PhoneValidationResult(true, canonical, Array.Empty<string>());
    }

    public async Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        // Immediate send from the application's configured sending number (so reconciliation can count it).
        var message = await InvokeAsync(ct => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null,
            contentRetention: null, addressRetention: null,
            smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null,
            scheduleType: null,
            sendAt: null,
            sendAsMms: null, contentVariables: null, riskCheck: null,
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: null,
            body: body,
            mediaUrl: null, contentSid: null,
            ct: ct), cancellationToken);

        return ToSendResult(message);
    }

    public async Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        // Scheduling is messaging-service only: use the MessagingServiceSid, ScheduleType=Fixed and SendAt.
        var message = await InvokeAsync(ct => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null,
            contentRetention: null, addressRetention: null,
            smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            sendAsMms: null, contentVariables: null, riskCheck: null,
            from: null,
            fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid,
            body: body,
            mediaUrl: null, contentSid: null,
            ct: ct), cancellationToken);

        return ToSendResult(message);
    }

    public async Task CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        // Cancel a not-yet-sent scheduled message.
        await InvokeAsync(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: ct), cancellationToken);
    }

    public async Task<SmsStatusResult> GetStatusAsync(string providerSid, CancellationToken cancellationToken)
    {
        var message = await InvokeAsync(ct => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerSid,
            ct: ct), cancellationToken);

        return new SmsStatusResult(message.Status?.Value, message.ErrorCode, message.ErrorMessage);
    }

    public async Task RedactContentAsync(string providerSid, CancellationToken cancellationToken)
    {
        // Blank the body at the provider (empty string), keeping the message record and its outcome intact.
        await InvokeAsync(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerSid,
            body: string.Empty,
            status: null,
            ct: ct), cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var fromNumber = _settings.FromNumber;
        var results = new List<ProviderMessage>();

        // One deadline for the whole paged reconciliation, linked to the caller's token.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);
        var deadline = cts.Token;

        int? page = null;
        string? pageToken = null;
        const int MaxPages = 1000; // backstop — never page unboundedly on the provider's cooperation alone.

        try
        {
            for (var i = 0; i < MaxPages; i++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: fromNumber,                 // ask the provider for THIS sending number's messages only.
                    dateSent: null,
                    dateSentQuery: to,                // wire DateSent<  → the range upper bound.
                    dateSentQueryQuery: from,         // wire DateSent>  → the range lower bound.
                    pageSize: 1000,
                    page: page,
                    pageToken: pageToken,
                    ct: deadline);

                if (response.Messages != null)
                {
                    foreach (var m in response.Messages)
                    {
                        results.Add(new ProviderMessage(m.Sid, m.Status?.Value, m.To, m.From, m.DateSent));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    break;
                }

                (page, pageToken) = ParseNextPage(response.NextPageUri);
                if (page is null && pageToken is null)
                {
                    break; // cannot advance — stop rather than loop forever.
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw; // the caller aborted — not a provider failure.
            }
            throw new SmsProviderException("The SMS provider is unreachable or timed out.", null, ex);
        }

        return results;
    }

    // --- helpers -----------------------------------------------------------------------------------------

    private SmsSendResult ToSendResult(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrEmpty(message.Sid))
        {
            throw new SmsProviderException("The SMS provider accepted the request but returned no message identifier.");
        }
        return new SmsSendResult(message.Sid, message.Status?.Value);
    }

    /// <summary>Run a single SDK call under a whole-call deadline and translate every failure into a
    /// <see cref="SmsProviderException"/> (or rethrow a genuine caller cancellation).</summary>
    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_budget);

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
            // A 2xx body that no longer matches the model — outcome unknown, surface as provider unavailable.
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw; // caller aborted — propagate the cancellation.
            }
            throw new SmsProviderException("The SMS provider is unreachable or timed out.", null, ex);
        }
    }

    private static SmsProviderException Translate(SdkException<RawError> ex)
    {
        var status = ex.Error.StatusCode;
        var code = TryReadProviderCode(ex.Error);
        var message = code.HasValue
            ? $"The SMS provider returned an error (HTTP {(int)status}, code {code})."
            : $"The SMS provider returned an error (HTTP {(int)status}).";
        return new SmsProviderException(message, status, ex);
    }

    // Best-effort extraction of the numeric provider error code only. The raw body is NOT surfaced — it can
    // contain the destination number.
    private static int? TryReadProviderCode(RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            return body?.Code;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsClientError(HttpStatusCode? status)
        => status.HasValue && (int)status.Value >= 400 && (int)status.Value < 500;

    private static IReadOnlyList<string> MapReasons(IReadOnlyList<ValidationError>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return new[] { "The number is not a usable destination." };
        }

        var reasons = new List<string>(errors.Count);
        foreach (var e in errors)
        {
            reasons.Add(e.Value);
        }
        return reasons;
    }

    // Parse Page and PageToken out of Twilio's relative NextPageUri query string.
    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        int? page = null;
        string? pageToken = null;

        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0 || queryStart == nextPageUri.Length - 1)
        {
            return (null, null);
        }

        var query = nextPageUri.Substring(queryStart + 1);
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }
            var key = pair.Substring(0, eq);
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));

            if (string.Equals(key, "Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
            {
                page = p;
            }
            else if (string.Equals(key, "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                pageToken = value;
            }
        }

        return (page, pageToken);
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
