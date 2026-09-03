using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Twilio;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Models;
using Twilio.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The Twilio-backed <see cref="ISmsGateway"/>. The only place the Twilio SDK is used. Every SDK failure
/// (all these operations throw <c>SdkException&lt;RawError&gt;</c>), transport failure, and unreadable body
/// is translated at this one boundary into <see cref="SmsGatewayException"/>. No shopper number or message
/// body is ever written to a log line here.
/// </summary>
public sealed class TwilioSmsGateway : ISmsGateway
{
    // A defensive cap so a misbehaving provider can never turn the reconciliation page loop into an
    // unbounded walk. 100 pages × 1000 messages is far beyond this account's own traffic.
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioClient client, TwilioSettings settings)
    {
        _client = client;
        _settings = settings;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneNumberValidation> ValidateAsync(string rawNumber, CancellationToken ct)
    {
        // Lookups resolves through its own Twilio host (not the messaging base URL) inside the SDK.
        var lookup = await InvokeAsync(() => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
            rawNumber,
            fields: null, countryCode: null, firstName: null, lastName: null, addressLine1: null,
            addressLine2: null, city: null, state: null, postalCode: null, addressCountryCode: null,
            nationalId: null, dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
            partnerSubId: null, ct: ct), "validate number", ct);

        var usable = lookup.Valid == true && !string.IsNullOrEmpty(lookup.PhoneNumber);
        return new PhoneNumberValidation(usable, usable ? lookup.PhoneNumber : null);
    }

    public async Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct)
    {
        var message = await InvokeAsync(
            () => CreateMessage(toNumber, body, from: _settings.FromNumber, messagingServiceSid: null, scheduleType: null, sendAt: null, ct),
            "send message", ct);
        return ToDispatchResult(message);
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        // Scheduling is a Messaging Service feature (schedule_type=fixed + send_at); it cannot use a bare 'from'.
        var message = await InvokeAsync(
            () => CreateMessage(toNumber, body, from: null, messagingServiceSid: _settings.MessagingServiceSid,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, ct),
            "schedule message", ct);
        return ToDispatchResult(message);
    }

    public async Task CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        await InvokeAsync(
            () => _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerSid,
                body: null, status: MessageEnumUpdateStatus.Canceled, ct: ct),
            "cancel scheduled message", ct);
    }

    public async Task<SmsDeliveryState> GetDeliveryStateAsync(string providerSid, CancellationToken ct)
    {
        var message = await InvokeAsync(
            () => _client.Api20100401Message.FetchMessage(_settings.AccountSid, providerSid, ct: ct),
            "read message", ct);
        return new SmsDeliveryState(message.Status?.Value, message.ErrorCode, message.ErrorMessage, ParseDate(message.DateSent), message.From);
    }

    public async Task RedactContentAsync(string providerSid, CancellationToken ct)
    {
        // Updating the body to empty redacts the text at the provider; the record and its outcome survive.
        await InvokeAsync(
            () => _client.Api20100401Message.UpdateMessage(_settings.AccountSid, providerSid,
                body: string.Empty, status: null, ct: ct),
            "redact message", ct);
    }

    public async Task<IReadOnlyList<ProviderMessageRecord>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ProviderMessageRecord>();
        string? pageToken = null;
        var pages = 0;

        do
        {
            // Wire mapping: DateSent> ← dateSentQueryQuery (lower bound), DateSent< ← dateSentQuery (upper bound),
            // From ← from (the configured sending number — asked of the provider, not filtered after the fact).
            var response = await InvokeAsync(
                () => _client.Api20100401Message.ListMessage(
                    _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ReconciliationPageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: ct),
                "list messages", ct);

            if (response.Messages is not null)
            {
                results.AddRange(response.Messages.Select(ToProviderRecord));
            }

            pageToken = ExtractPageToken(response.NextPageUri);
            pages++;
        }
        while (pageToken is not null && pages < MaxReconciliationPages);

        return results;
    }

    // ----------------------------------------------------------------- SDK plumbing

    private Task<ApiV2010AccountMessage> CreateMessage(
        string to, string body, string? from, string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, CancellationToken ct)
        => _client.Api20100401Message.CreateMessage(
            _settings.AccountSid, to,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: scheduleType, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: from, fallbackFrom: null,
            messagingServiceSid: messagingServiceSid, body: body, mediaUrl: null, contentSid: null,
            ct: ct);

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message)
        => new(message.Sid ?? string.Empty, message.From, message.Status?.Value, ParseDate(message.DateSent));

    private static ProviderMessageRecord ToProviderRecord(ApiV2010AccountMessage message)
        => new(message.Sid ?? string.Empty, message.To, message.From, message.Status?.Value, ParseDate(message.DateSent));

    /// <summary>
    /// The single error boundary. Every messaging/lookup call goes through here so that provider errors,
    /// transport failures, and malformed/drifted response bodies all surface as one exception type.
    /// A JsonException arrives from two directions (a drifted 2xx body, or an error body that does not
    /// match its generated shape), so it is caught explicitly; the SDK-exception-only ladder would miss it.
    /// </summary>
    private static async Task<T> InvokeAsync<T>(Func<Task<T>> call, string operation, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, operation);
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException($"The provider returned a response for '{operation}' that could not be processed.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SmsGatewayException($"The provider was unreachable during '{operation}'.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // The SDK's own per-attempt timeout, not the caller's cancellation (which propagates untouched).
            throw new SmsGatewayException($"The provider call for '{operation}' timed out.", null, ex);
        }
    }

    private static SmsGatewayException Translate(SdkException<RawError> ex, string operation)
    {
        var status = ex.Error.StatusCode;
        var code = TryReadTwilioCode(ex.Error);
        // Deliberately excludes the raw provider body: it can echo the destination number.
        var message = code is not null
            ? $"The provider rejected '{operation}' (HTTP {(int)status}, Twilio code {code})."
            : $"The provider rejected '{operation}' (HTTP {(int)status}).";
        return new SmsGatewayException(message, status, ex);
    }

    private static int? TryReadTwilioCode(RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            return body?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        var queryStart = nextPageUri.IndexOf('?');
        var query = queryStart >= 0 ? nextPageUri[(queryStart + 1)..] : nextPageUri;
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            if (pair[..eq].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                var value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                return string.IsNullOrEmpty(value) ? null : value;
            }
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;

    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; init; }
    }
}
