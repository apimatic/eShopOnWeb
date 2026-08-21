using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.Messaging;

public class TwilioMessagingGateway : ISmsGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public string FromNumber => _settings.FromNumber;

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        var response = await InvokeAsync(
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
            isWrite: false,
            cancellationToken);

        if (response.Valid == false)
        {
            var reasons = response.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", response.ValidationErrors.Select(e => e.Value))
                : "The number is not in a range a carrier can assign.";
            return new PhoneLookupResult(false, response.PhoneNumber, reasons);
        }

        if (string.IsNullOrWhiteSpace(response.PhoneNumber))
        {
            return new PhoneLookupResult(false, null, "The provider did not return a canonical number.");
        }

        return new PhoneLookupResult(true, response.PhoneNumber, null);
    }

    public Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken)
        => CreateAsync(to, body, from: _settings.FromNumber, messagingServiceSid: null, scheduleType: null, sendAt: null, cancellationToken);

    public Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => CreateAsync(
            to,
            body,
            from: _settings.FromNumber,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            cancellationToken);

    public async Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        var message = await InvokeAsync(
            ct => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct),
            isWrite: true,
            cancellationToken);

        return Map(message, accepted: true, failureReason: null);
    }

    public async Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        var message = await InvokeAsync(
            ct => _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct),
            isWrite: false,
            cancellationToken);

        return Map(message, accepted: true, failureReason: null);
    }

    public async Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        var message = await InvokeAsync(
            ct => _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                body: "",
                status: null,
                requestOptions: null,
                ct: ct),
            isWrite: true,
            cancellationToken);

        return Map(message, accepted: true, failureReason: null);
    }

    public async Task<IReadOnlyList<SmsMessageResult>> ListSentFromAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken)
    {
        var results = new List<SmsMessageResult>();
        const int pageSize = 1000;
        const int maxPages = 20;
        int page = 0;
        string? pageToken = null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(45));

        while (page < maxPages)
        {
            var capturedPage = page;
            var capturedToken = pageToken;
            var response = await InvokeAsync(
                ct => _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: toExclusive,
                    dateSentQueryQuery: fromInclusive,
                    pageSize: pageSize,
                    page: capturedPage,
                    pageToken: capturedToken,
                    requestOptions: null,
                    ct: ct),
                isWrite: false,
                cts.Token);

            var messages = response.Messages ?? Array.Empty<ApiV2010AccountMessage>();
            foreach (var message in messages)
            {
                results.Add(Map(message, accepted: true, failureReason: null));
            }

            if (messages.Count < pageSize || string.IsNullOrEmpty(response.NextPageUri))
            {
                break;
            }

            pageToken = ExtractPageToken(response.NextPageUri);
            page++;
        }

        if (page >= maxPages)
        {
            _logger.LogWarning("Reconciliation listing stopped after {MaxPages} pages.", maxPages);
        }

        return results;
    }

    private async Task<SmsMessageResult> CreateAsync(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var message = await InvokeAsync(
            ct => _client.Api20100401Message.CreateMessage(
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
                requestOptions: null,
                ct: ct),
            isWrite: true,
            cancellationToken);

        return Map(message, accepted: true, failureReason: null);
    }

    private async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> call, bool isWrite, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        using var writeScope = isWrite ? SingleAttemptPostHandler.BeginWriteScope() : null;

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            _logger.LogWarning("Messaging provider returned HTTP {StatusCode}.", (int)status);
            throw new SmsProviderException("The messaging provider rejected the request.", status, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Messaging provider returned a response that could not be processed.");
            throw new SmsProviderException("The messaging provider returned a response that could not be processed.", ex);
        }
        catch (DuplicateProviderWriteException ex)
        {
            _logger.LogWarning("A duplicate messaging-provider write was refused.");
            throw new SmsProviderException("The messaging provider write outcome is unknown.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            _logger.LogWarning("Messaging provider is unreachable.");
            throw new SmsProviderException("The messaging provider is unreachable.", ex);
        }
    }

    private static SmsMessageResult Map(ApiV2010AccountMessage message, bool accepted, string? failureReason)
    {
        return new SmsMessageResult(
            accepted,
            message.Sid,
            message.Status?.Value,
            message.To,
            message.From,
            message.Body,
            message.DateSent,
            message.ErrorCode,
            message.ErrorMessage,
            message.Direction?.Value,
            failureReason);
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri) || !Uri.TryCreate(nextPageUri, UriKind.RelativeOrAbsolute, out var uri))
        {
            return null;
        }

        var query = uri.IsAbsoluteUri ? uri.Query : nextPageUri[(nextPageUri.IndexOf('?') >= 0 ? nextPageUri.IndexOf('?') : 0)..];
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
