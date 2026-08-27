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
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "line_type_intelligence,line_status",
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
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .ToArray() ?? Array.Empty<string>();

            var usable = response.Valid == true
                && errors.Length == 0
                && !string.IsNullOrWhiteSpace(response.PhoneNumber);

            return new PhoneLookupResult(
                usable,
                usable ? response.PhoneNumber : null,
                errors,
                usable ? null : "The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex) when (IsCallerNumberRejection(ex.Error.StatusCode))
        {
            return new PhoneLookupResult(false, null, Array.Empty<string>(), "The number is not a usable destination.");
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex);
        }
        catch (JsonException ex)
        {
            throw new ProviderUnavailableException("The provider returned a response that could not be processed.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderUnavailableException("The messaging provider is unreachable.", inner: ex);
        }
    }

    public Task<MessageSendResult> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        return InvokeWrite(ct => _client.Api20100401Message.CreateMessage(
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
            scheduleType: sendAt.HasValue ? MessageEnumScheduleType.Fixed : null,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: sendAt.HasValue ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: ct), cancellationToken);
    }

    public async Task<MessageSendResult?> FetchAsync(string sid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _settings.AccountSid,
                    sid: sid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);

            return ToSendResult(message, succeeded: true, failureReason: null);
        }
        catch (SdkException<RawError> ex)
        {
            return ToSendResult(null, succeeded: false, DescribeRawError(ex.Error));
        }
        catch (JsonException)
        {
            return ToSendResult(null, succeeded: false, "The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ToSendResult(null, succeeded: false, "The messaging provider is unreachable.");
        }
    }

    public Task<MessageSendResult> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
    {
        return InvokeWrite(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: sid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: ct), cancellationToken);
    }

    public Task<MessageSendResult> RedactBodyAsync(string sid, CancellationToken cancellationToken)
    {
        return InvokeWrite(ct => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: sid,
            body: "",
            status: null,
            requestOptions: null,
            ct: ct), cancellationToken);
    }

    public async Task<ProviderMessagePage> ListFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var messages = new List<ProviderMessage>();
        string? pageToken = null;
        var incomplete = false;
        const int MaxPages = 50;
        var pages = 0;

        try
        {
            while (pages < MaxPages)
            {
                var page = await Bounded(
                    ct => _client.Api20100401Message.ListMessage(
                        accountSid: _settings.AccountSid,
                        to: null,
                        from: _settings.FromNumber,
                        dateSent: null,
                        dateSentQuery: to,
                        dateSentQueryQuery: from,
                        pageSize: 1000,
                        page: null,
                        pageToken: pageToken,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        if (string.IsNullOrWhiteSpace(message.Sid))
                        {
                            continue;
                        }

                        messages.Add(new ProviderMessage(
                            message.Sid,
                            message.Status?.Value,
                            message.Body,
                            message.DateSent,
                            message.DateCreated));
                    }
                }

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                {
                    break;
                }

                pageToken = TryGetPageToken(page.NextPageUri);
                if (pageToken is null)
                {
                    incomplete = true;
                    break;
                }

                pages++;
                if (pages >= MaxPages)
                {
                    incomplete = true;
                }
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw ToProviderException(ex);
        }
        catch (JsonException ex)
        {
            throw new ProviderUnavailableException("The provider returned a response that could not be processed.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ProviderUnavailableException("The messaging provider is unreachable.", inner: ex);
        }

        return new ProviderMessagePage(messages, incomplete);
    }

    private async Task<MessageSendResult> InvokeWrite(
        Func<CancellationToken, Task<ApiV2010AccountMessage>> call,
        CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnce.Begin())
            {
                var message = await Bounded(call, cancellationToken);
                return ToSendResult(message, succeeded: true, failureReason: null);
            }
        }
        catch (TwilioWriteOnceViolationException)
        {
            return ToSendResult(null, succeeded: false, "The write was not retried; outcome is unknown.");
        }
        catch (SdkException<RawError> ex)
        {
            return ToSendResult(null, succeeded: false, DescribeRawError(ex.Error));
        }
        catch (JsonException)
        {
            return ToSendResult(null, succeeded: false, "The provider returned a response that could not be processed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ToSendResult(null, succeeded: false, "The messaging provider is unreachable.");
        }
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static MessageSendResult ToSendResult(ApiV2010AccountMessage? message, bool succeeded, string? failureReason)
    {
        return new MessageSendResult(
            succeeded,
            message?.Sid,
            message?.Status?.Value,
            message?.ErrorCode,
            message?.ErrorMessage,
            failureReason);
    }

    private static bool IsCallerNumberRejection(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        return status is >= 400 and < 500 and not 401 and not 403 and not 429;
    }

    private static ProviderUnavailableException ToProviderException(SdkException<RawError> ex)
    {
        var status = ex.Error.StatusCode;
        var code = (int)status;
        if (code is 401 or 403)
        {
            return new ProviderUnavailableException("Provider unavailable.", status, ex);
        }

        if (code == 429)
        {
            return new ProviderUnavailableException("Temporarily unavailable.", status, ex);
        }

        return new ProviderUnavailableException("The messaging provider is unavailable.", status, ex);
    }

    private static string DescribeRawError(RawError error)
    {
        try
        {
            var body = error.ReadAsString();
            return string.IsNullOrWhiteSpace(body)
                ? "The messaging provider rejected the request."
                : "The messaging provider rejected the request.";
        }
        catch (JsonException)
        {
            return "The messaging provider rejected the request.";
        }
    }

    private static string? TryGetPageToken(string nextPageUri)
    {
        Uri uri;
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            uri = absolute;
        }
        else if (Uri.TryCreate("https://api.twilio.com" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri), UriKind.Absolute, out var relative))
        {
            uri = relative;
        }
        else
        {
            return null;
        }

        var query = uri.Query.TrimStart('?');
        if (string.IsNullOrEmpty(query))
        {
            return null;
        }

        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }
}
