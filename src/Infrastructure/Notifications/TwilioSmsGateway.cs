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
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// The Twilio-backed implementation of <see cref="ISmsGateway"/>. Every provider outcome is translated
/// into a plain result or an <see cref="SmsGatewayException"/>; no Twilio type escapes this class. It
/// never logs a destination number, message body, or the auth token.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    /// <summary>Reconciliation must not walk the provider's full history unbounded.</summary>
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupNumberAsync(string rawPhoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawPhoneNumber,
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

            var isValid = response.Valid == true;
            return new PhoneLookupResult(isValid, response.PhoneNumber);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            // The provider could not resolve the number at all — a definitively unusable destination,
            // not an outage. Reject it (rather than masking an outage as "invalid").
            return new PhoneLookupResult(false, null);
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, "phone lookup");
        }
        catch (JsonException ex)
        {
            throw new SmsGatewayException("The provider returned a phone-lookup response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException("The provider was unreachable during phone lookup.", innerException: ex);
        }
    }

    public Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct)
    {
        return InvokeAsync(async () =>
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
                ct: ct);

            return ToSendResult(response);
        }, "message send");
    }

    public Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        return InvokeAsync(async () =>
        {
            // Scheduling is Messaging-Services-only: the messaging service selects the sender, so `from`
            // must be omitted and a MessagingServiceSid supplied.
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
                ct: ct);

            return ToSendResult(response);
        }, "message schedule");
    }

    public Task<SmsSendResult> CancelScheduledAsync(string providerMessageSid, CancellationToken ct)
    {
        return InvokeAsync(async () =>
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);

            return ToSendResult(response);
        }, "scheduled message cancel");
    }

    public Task<SmsStatusResult> FetchStatusAsync(string providerMessageSid, CancellationToken ct)
    {
        return InvokeAsync(async () =>
        {
            var response = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: ct);

            return new SmsStatusResult(StatusString(response.Status), response.ErrorCode, response.ErrorMessage);
        }, "message status fetch");
    }

    public Task RedactContentAsync(string providerMessageSid, CancellationToken ct)
    {
        return InvokeAsync(async () =>
        {
            // Redact the body text at the provider (empty body). This deliberately does NOT delete the
            // message resource, so the record that a message was sent — and its outcome — survives.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: ct);

            return true;
        }, "message content redaction");
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ProviderMessage>();
        string? pageToken = null;
        var page = 0;

        while (page < MaxReconciliationPages)
        {
            var response = await InvokeAsync(() => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,          // provider-side filter to this application's number
                dateSent: null,
                dateSentQuery: to,                    // wire DateSent< (on/before)
                dateSentQueryQuery: from,             // wire DateSent> (on/after)
                pageSize: ReconciliationPageSize,
                page: null,
                pageToken: pageToken,
                ct: ct), "message list");

            var messages = response.Messages;
            if (messages is not null)
            {
                foreach (var message in messages)
                {
                    if (message.Sid is null)
                    {
                        continue;
                    }
                    results.Add(new ProviderMessage(
                        message.Sid,
                        StatusString(message.Status),
                        message.From,
                        message.To,
                        ParseDateSent(message.DateSent)));
                }
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }
            pageToken = ExtractPageToken(response.NextPageUri);
            if (string.IsNullOrEmpty(pageToken))
            {
                break;
            }
            page++;
        }

        return results;
    }

    // --- translation helpers -------------------------------------------------------------------

    private static async Task<T> InvokeAsync<T>(Func<Task<T>> call, string action)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            throw Translate(ex, action);
        }
        catch (JsonException ex)
        {
            // A 2xx whose body no longer matches the model: outcome genuinely unknown.
            throw new SmsGatewayException($"The provider returned a {action} response that could not be processed.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsGatewayException($"The provider was unreachable during {action}.", innerException: ex);
        }
    }

    private static SmsGatewayException Translate(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        int? providerCode = null;
        try
        {
            // Best-effort read of the provider's own error code. Deliberately do not surface the
            // provider's error message text — it can echo the destination number.
            var body = ex.Error.ReadAsJson<TwilioErrorBody>();
            providerCode = body?.Code;
        }
        catch
        {
            // Error body was not the expected JSON shape — status code alone is enough.
        }

        return new SmsGatewayException($"Twilio {action} failed with HTTP status {status}.", status, providerCode, ex);
    }

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message) =>
        new(message.Sid, StatusString(message.Status), message.ErrorCode, message.ErrorMessage);

    private static string StatusString(MessageEnumStatus? status) => status?.Value ?? "unknown";

    private static DateTimeOffset? ParseDateSent(string? dateSent) =>
        DateTimeOffset.TryParse(dateSent, out var value) ? value : null;

    private static string? ExtractPageToken(string nextPageUri)
    {
        const string marker = "PageToken=";
        var index = nextPageUri.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }
        var start = index + marker.Length;
        var end = nextPageUri.IndexOf('&', start);
        var token = end < 0 ? nextPageUri[start..] : nextPageUri[start..end];
        return Uri.UnescapeDataString(token);
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }
    }
}
