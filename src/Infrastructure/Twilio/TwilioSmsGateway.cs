using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
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

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio implementation of <see cref="ISmsGateway"/> — the sole place the Twilio SDK is used. Every SDK
/// operation here is a Case-B call (<c>SdkException&lt;RawError&gt;</c>); failures are translated to the
/// provider-agnostic <see cref="SmsGatewayException"/> at this boundary, carrying the HTTP status only.
/// The shopper's phone number and the auth token are never logged or echoed in an error.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const int MaxReconciliationPages = 50;
    private const long ReconciliationPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string rawPhoneNumber, CancellationToken ct = default)
    {
        try
        {
            // Lookup resolves through a DIFFERENT Twilio host than messaging (Twilio:BaseUrl governs
            // messaging only), so this validation is unaffected by a messaging base-URL override.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawPhoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, ct: ct);

            var valid = response.Valid == true;
            return new PhoneValidationResult(valid, valid ? response.PhoneNumber : null, response.NationalFormat);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            if (status is >= 400 and < 500)
            {
                // e.g. a 404 on an unparseable number — this IS the "reject at registration" outcome,
                // not a gap and not a 500.
                return new PhoneValidationResult(false, null, null);
            }
            throw Translate(ex);
        }
        catch (JsonException ex)
        {
            // A parse failure is "could not validate", NOT a domain "invalid number" — never turn it into a rejection.
            throw new SmsGatewayException("The number could not be validated right now.", statusCode: null, innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            throw new SmsGatewayException("The number could not be validated right now (provider unreachable).", statusCode: null, innerException: ex);
        }
    }

    public async Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        try
        {
            using (SingleSendGuardHandler.BeginSingleSend())
            {
                var message = await _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toNumber,
                    statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                    attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                    addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                    shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
                    contentVariables: null, riskCheck: null,
                    from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                    body: body, mediaUrl: null, contentSid: null, ct: ct);

                return ToDispatchResult(message);
            }
        }
        catch (Exception ex)
        {
            throw TranslateSend(ex);
        }
    }

    public async Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAtUtc, CancellationToken ct = default)
    {
        try
        {
            using (SingleSendGuardHandler.BeginSingleSend())
            {
                // Provider-side scheduling is a Messaging-Service feature: supply the messaging service
                // (not a From number), ScheduleType=Fixed and SendAt.
                var message = await _client.Api20100401Message.CreateMessage(
                    accountSid: _settings.AccountSid,
                    to: toNumber,
                    statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                    attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                    addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                    shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAtUtc, sendAsMms: null,
                    contentVariables: null, riskCheck: null,
                    from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                    body: body, mediaUrl: null, contentSid: null, ct: ct);

                return ToDispatchResult(message);
            }
        }
        catch (Exception ex)
        {
            throw TranslateSend(ex);
        }
    }

    public async Task CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);
        }
        catch (Exception ex)
        {
            throw TranslateSend(ex);
        }
    }

    public async Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid, sid: messageSid, ct: ct);

            return new SmsStatusResult(message.Status?.Value, FormatErrorCode(message.ErrorCode));
        }
        catch (Exception ex)
        {
            throw TranslateSend(ex);
        }
    }

    public async Task RedactContentAsync(string messageSid, CancellationToken ct = default)
    {
        try
        {
            // Updating the body to an empty string redacts the message text at the provider while the
            // resource and its metadata (status, dates, error) survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: messageSid,
                body: string.Empty,
                status: null,
                ct: ct);
        }
        catch (Exception ex)
        {
            throw TranslateSend(ex);
        }
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        var collected = new List<ProviderMessage>();
        int page = 0;
        string? pageToken = null;

        for (var pageIndex = 0; pageIndex < MaxReconciliationPages; pageIndex++)
        {
            ListMessageResponse response;
            try
            {
                response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,           // server-side From filter = our configured sending number
                    dateSent: null,
                    dateSentQuery: toUtc,                  // wire DateSent< : sent on/before range end
                    dateSentQueryQuery: fromUtc,           // wire DateSent> : sent on/after range start
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: ct);
            }
            catch (Exception ex)
            {
                throw TranslateSend(ex);
            }

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    if (string.IsNullOrEmpty(message.Sid))
                    {
                        continue;
                    }
                    collected.Add(new ProviderMessage(
                        message.Sid!,
                        message.Status?.Value,
                        ParseTwilioDate(message.DateSent),
                        FormatErrorCode(message.ErrorCode)));
                }
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            var nextToken = ExtractQueryParameter(response.NextPageUri, "PageToken");
            if (string.IsNullOrEmpty(nextToken))
            {
                // Cannot advance safely without the provider's page token; stop rather than loop or truncate silently.
                break;
            }
            pageToken = nextToken;
            page = (response.Page ?? page) + 1;
        }

        return collected;
    }

    // ---------------------------------------------------------------- helpers

    private static SmsDispatchResult ToDispatchResult(ApiV2010AccountMessage message)
    {
        var sid = message.Sid
            ?? throw new SmsGatewayException("The provider accepted the message but returned no identifier.");
        return new SmsDispatchResult(sid, message.Status?.Value);
    }

    private static string? FormatErrorCode(int? errorCode) =>
        errorCode?.ToString(CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTwilioDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string? ExtractQueryParameter(string uri, string key)
    {
        var questionMark = uri.IndexOf('?');
        if (questionMark < 0 || questionMark == uri.Length - 1)
        {
            return null;
        }

        var query = uri[(questionMark + 1)..];
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }
            var name = pair[..equals];
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
            }
        }
        return null;
    }

    /// <summary>Translate any exception from a send/read call into a caller-safe <see cref="SmsGatewayException"/>.</summary>
    private static SmsGatewayException TranslateSend(Exception ex) => ex switch
    {
        SmsGatewayException already => already,
        SdkException<RawError> sdk => Translate(sdk),
        DuplicateSendBlockedException dup => new SmsGatewayException(
            "Send outcome is unknown: a transport-level retry was blocked to avoid a duplicate message.", statusCode: null, innerException: dup),
        JsonException json => new SmsGatewayException(
            "The provider returned a response that could not be processed.", statusCode: null, innerException: json),
        HttpRequestException or TaskCanceledException or OperationCanceledException => new SmsGatewayException(
            "The SMS provider was unreachable.", statusCode: null, innerException: ex),
        _ => new SmsGatewayException("An unexpected error occurred talking to the SMS provider.", statusCode: null, innerException: ex)
    };

    private static SmsGatewayException Translate(SdkException<RawError> ex)
    {
        var status = (int)ex.Error.StatusCode;
        var providerCode = TryReadProviderCode(ex.Error);
        var message = providerCode is not null
            ? $"The SMS provider rejected the request (provider code {providerCode})."
            : "The SMS provider returned an error.";
        return new SmsGatewayException(message, status, ex);
    }

    /// <summary>Best-effort read of Twilio's numeric error code only — never the message, which can echo the number.</summary>
    private static string? TryReadProviderCode(RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            return body?.Code?.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
