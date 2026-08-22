using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.TwilioMessaging;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(
        TwilioSdkClient client,
        IOptions<TwilioOptions> options,
        ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string FromNumber => _options.FromNumber;

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct)
    {
        try
        {
            var response = await Bounded(ct, token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
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
                ct: token));

            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .ToArray() ?? Array.Empty<string>();

            return new PhoneLookupResult(
                response.Valid == true,
                response.PhoneNumber,
                errors);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "lookup");
        }
    }

    public Task<SmsMessageSnapshot> SendAsync(string toCanonicalNumber, string body, CancellationToken ct)
    {
        return CreateAsync(toCanonicalNumber, body, scheduleType: null, sendAt: null, messagingServiceSid: null, ct);
    }

    public Task<SmsMessageSnapshot> ScheduleAsync(
        string toCanonicalNumber,
        string body,
        DateTimeOffset sendAt,
        CancellationToken ct)
    {
        return CreateAsync(
            toCanonicalNumber,
            body,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt.ToUniversalTime(),
            messagingServiceSid: _options.MessagingServiceSid,
            ct);
    }

    public async Task<SmsMessageSnapshot> FetchAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            var message = await Bounded(ct, token => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: providerSid,
                ct: token));
            return Map(message);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "fetch");
        }
    }

    public async Task<SmsMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            using (AtMostOncePostHandler.BeginPostScope())
            {
                var message = await Bounded(ct, token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: token));
                return Map(message);
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, "cancel");
        }
    }

    public async Task<SmsMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken ct)
    {
        try
        {
            using (AtMostOncePostHandler.BeginPostScope())
            {
                var message = await Bounded(ct, token => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    body: "",
                    status: null,
                    ct: token));
                return Map(message);
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, "redact");
        }
    }

    public async Task<SmsMessageList> ListFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        try
        {
            var messages = new List<SmsMessageSnapshot>();
            string? pageToken = null;
            int? page = null;
            var truncated = false;

            for (var pages = 0; pages < MaxListPages; pages++)
            {
                var capturedPageToken = pageToken;
                var capturedPage = page;
                var response = await Bounded(ct, token => _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: capturedPage,
                    pageToken: capturedPageToken,
                    ct: token));

                if (response.Messages is not null)
                {
                    foreach (var message in response.Messages)
                    {
                        messages.Add(Map(message));
                    }
                }

                if (string.IsNullOrEmpty(response.NextPageUri))
                {
                    return new SmsMessageList(messages, Truncated: false, _options.FromNumber);
                }

                pageToken = ExtractQueryValue(response.NextPageUri, "PageToken")
                    ?? ExtractQueryValue(response.NextPageUri, "pageToken");
                var pageValue = ExtractQueryValue(response.NextPageUri, "Page")
                    ?? ExtractQueryValue(response.NextPageUri, "page");
                page = int.TryParse(pageValue, out var parsedPage) ? parsedPage : pages + 1;
            }

            truncated = true;
            _logger.LogWarning("Reconciliation list reached the page cap of {MaxPages}.", MaxListPages);
            return new SmsMessageList(messages, truncated, _options.FromNumber);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "list");
        }
    }

    private async Task<SmsMessageSnapshot> CreateAsync(
        string toCanonicalNumber,
        string body,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        string? messagingServiceSid,
        CancellationToken ct)
    {
        try
        {
            using (AtMostOncePostHandler.BeginPostScope())
            {
                var message = await Bounded(ct, token => _client.Api20100401Message.CreateMessage(
                    accountSid: _options.AccountSid,
                    to: toCanonicalNumber,
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
                    from: _options.FromNumber,
                    fallbackFrom: null,
                    messagingServiceSid: messagingServiceSid,
                    body: body,
                    mediaUrl: null,
                    contentSid: null,
                    ct: token));

                _logger.LogInformation("Created provider message {ProviderSid} with status {Status}.", message.Sid, message.Status?.Value);
                return Map(message);
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex, "create");
        }
    }

    private async Task<T> Bounded<T>(CancellationToken ct, Func<CancellationToken, Task<T>> call)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private SmsGatewayException Translate(Exception ex, string operation)
    {
        switch (ex)
        {
            case SdkException<RawError> sdk:
                _logger.LogWarning(
                    "Twilio {Operation} failed with HTTP {Status}.",
                    operation,
                    (int)sdk.Error.StatusCode);
                return new SmsGatewayException(
                    "The messaging provider rejected the request.",
                    sdk.Error.StatusCode,
                    sdk);

            case DuplicatePostRefusedException:
                _logger.LogWarning("Twilio {Operation} retry was refused after the first attempt.", operation);
                return new SmsGatewayException(
                    "The messaging write may already have been accepted; the outcome is unknown.",
                    null,
                    ex);

            case JsonException:
                _logger.LogWarning("Twilio {Operation} returned a response that could not be processed.", operation);
                return new SmsGatewayException(
                    "The provider returned a response that could not be processed.",
                    null,
                    ex);

            case HttpRequestException:
            case TaskCanceledException:
                _logger.LogWarning("Twilio {Operation} failed because the provider could not be reached.", operation);
                return new SmsGatewayException("The messaging provider could not be reached.", null, ex);

            default:
                _logger.LogWarning("Twilio {Operation} failed unexpectedly.", operation);
                return new SmsGatewayException("The messaging provider request failed.", null, ex);
        }
    }

    private static SmsMessageSnapshot Map(ApiV2010AccountMessage message)
    {
        return new SmsMessageSnapshot(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            ErrorMessage: null,
            message.Body,
            message.From,
            message.To,
            message.DateCreated,
            message.DateSent,
            message.Direction?.Value);
    }

    private static string? ExtractQueryValue(string nextPageUri, string key)
    {
        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        var query = nextPageUri[(queryIndex + 1)..];
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..eq]);
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(part[(eq + 1)..]);
            }
        }

        return null;
    }
}
