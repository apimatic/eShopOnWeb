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
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Sms;

/// <summary>
/// The one place the Twilio SDK is called. It owns the configured account, sending number and
/// messaging-service SID, translates every SDK/transport failure into <see cref="SmsGatewayException"/>,
/// and never logs a destination number or message body.
/// </summary>
public class TwilioSmsGateway : ISmsGateway
{
    private const int MaxReconciliationPages = 100;
    private const long ReconciliationPageSize = 200;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string SendingNumber => _settings.FromNumber;

    public async Task<PhoneValidationResult> ValidateNumberAsync(string rawPhoneNumber, CancellationToken ct = default)
    {
        try
        {
            // Lookup resolves against the separate lookups host — the Twilio:BaseUrl messaging override
            // does not touch it.
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawPhoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null, postalCode: null,
                addressCountryCode: null, nationalId: null, dateOfBirth: null, lastVerifiedDate: null,
                verificationSid: null, partnerSubId: null,
                ct: ct);

            return new PhoneValidationResult(response.Valid == true, response.PhoneNumber);
        }
        catch (SdkException<RawError> ex)
        {
            // A number the provider cannot resolve comes back as 404 — that is "not usable", not a fault.
            if ((int)ex.Error.StatusCode == 404)
                return new PhoneValidationResult(false, null);
            throw ProviderError(ex, "phone-number lookup");
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex, "phone-number lookup");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex, "phone-number lookup");
        }
    }

    public Task<SmsSendResult> SendAsync(string toPhoneNumber, string body, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            // Immediate send from the configured sending number, so reconciliation (which filters by
            // that number) can find it.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
                contentVariables: null, riskCheck: null,
                from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            return new SmsSendResult(message.Sid, message.Status?.Value ?? "unknown", message.ErrorCode, message.ErrorMessage);
        }, "send message", ct);

    public Task<SmsSendResult> ScheduleAsync(string toPhoneNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            // Scheduling is a Messaging-Service capability: the provider holds the message until sendAt.
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: toPhoneNumber,
                statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
                attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
                addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
                shortenUrls: null,
                scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt,
                sendAsMms: null, contentVariables: null, riskCheck: null,
                from: null, fallbackFrom: null, messagingServiceSid: _settings.MessagingServiceSid,
                body: body, mediaUrl: null, contentSid: null,
                ct: ct);

            return new SmsSendResult(message.Sid, message.Status?.Value ?? "unknown", message.ErrorCode, message.ErrorMessage);
        }, "schedule message", ct);

    public Task CancelScheduledAsync(string providerMessageId, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: ct);
            return true;
        }, "cancel scheduled message", ct);

    public Task<SmsDeliveryState> GetDeliveryStateAsync(string providerMessageId, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                ct: ct);
            return new SmsDeliveryState(message.Status?.Value ?? "unknown", message.ErrorCode, message.ErrorMessage);
        }, "fetch message", ct);

    public Task RedactContentAsync(string providerMessageId, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            // Redact the body at the provider (empty body) so the text is no longer retrievable there,
            // while the message record and its status survive.
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: string.Empty,
                status: null,
                ct: ct);
            return true;
        }, "redact message", ct);

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        InvokeAsync(async () =>
        {
            var results = new List<ProviderMessageRecord>();
            int? page = null;
            string? pageToken = null;
            int pageCount = 0;

            while (true)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,      // ask the provider for only OUR number's messages
                    dateSent: null,
                    dateSentQuery: to,               // wire DateSent<  (on/before the range end)
                    dateSentQueryQuery: from,        // wire DateSent>  (on/after the range start)
                    pageSize: ReconciliationPageSize,
                    page: page,
                    pageToken: pageToken,
                    ct: ct);

                if (response.Messages is not null)
                {
                    foreach (var m in response.Messages)
                    {
                        results.Add(new ProviderMessageRecord(
                            m.Sid ?? string.Empty,
                            m.Status?.Value ?? "unknown",
                            m.To,
                            m.ErrorCode,
                            m.ErrorMessage,
                            m.DateSent));
                    }
                }

                // Bound the loop on our own terms — never rely solely on the provider to stop us.
                if (++pageCount >= MaxReconciliationPages)
                {
                    _logger.LogWarning("Reconciliation stopped at the {0}-page cap; results may be truncated.", MaxReconciliationPages);
                    break;
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                    break;

                var (nextPage, nextToken) = ParseNextPage(response.NextPageUri!);
                if (nextPage is null && nextToken is null)
                    break;

                page = nextPage;
                pageToken = nextToken;
            }

            return (IReadOnlyList<ProviderMessageRecord>)results;
        }, "list messages", ct);

    // ----- error translation ---------------------------------------------------------------

    private async Task<T> InvokeAsync<T>(Func<Task<T>> operation, string action, CancellationToken ct)
    {
        try
        {
            return await operation();
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderError(ex, action);
        }
        catch (JsonException ex)
        {
            throw Unreadable(ex, action);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw Unreachable(ex, action);
        }
    }

    private SmsGatewayException ProviderError(SdkException<RawError> ex, string action)
    {
        var status = (int)ex.Error.StatusCode;
        _logger.LogWarning("Twilio {0} returned HTTP {1}.", action, status);
        return new SmsGatewayException($"Twilio {action} failed with status {status}.", status, ex);
    }

    private SmsGatewayException Unreadable(JsonException ex, string action)
    {
        _logger.LogWarning("Twilio {0} returned an unreadable response.", action);
        return new SmsGatewayException($"Twilio {action} returned a response that could not be processed.", ex);
    }

    private SmsGatewayException Unreachable(Exception ex, string action)
    {
        _logger.LogWarning("Twilio {0} was unreachable.", action);
        return new SmsGatewayException($"Twilio {action} is unreachable.", ex);
    }

    /// <summary>Extracts the Page/PageToken query values from a provider next-page URI.</summary>
    private static (int? Page, string? PageToken) ParseNextPage(string nextPageUri)
    {
        int? page = null;
        string? token = null;

        var qIndex = nextPageUri.IndexOf('?');
        if (qIndex < 0 || qIndex == nextPageUri.Length - 1)
            return (null, null);

        foreach (var pair in nextPageUri.Substring(qIndex + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq < 0)
                continue;

            var key = Uri.UnescapeDataString(pair.Substring(0, eq));
            var value = Uri.UnescapeDataString(pair.Substring(eq + 1));

            if (key.Equals("Page", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var p))
                page = p;
            else if (key.Equals("PageToken", StringComparison.OrdinalIgnoreCase))
                token = value;
        }

        return (page, token);
    }
}
