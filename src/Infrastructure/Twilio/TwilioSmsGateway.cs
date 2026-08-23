using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioSmsGateway : ISmsGateway
{
    private static readonly TimeSpan PerCallBudget = TimeSpan.FromSeconds(15);
    private const int MaxListPages = 20;

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

    public async Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken)
    {
        try
        {
            var response = await Bounded(
                ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: rawNumber,
                    fields: "line_type_intelligence,line_status",
                    countryCode: rawNumber.TrimStart().StartsWith('+') ? null : null,
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

            var lineType = response.LineTypeIntelligence?.Type;
            _logger.LogInformation(
                "Twilio lookup completed. Valid={Valid} LineType={LineType} ValidationErrorCount={ValidationErrorCount} LineTypeErrorCode={LineTypeErrorCode}",
                response.Valid,
                string.IsNullOrWhiteSpace(lineType) ? "(none)" : lineType,
                response.ValidationErrors?.Count ?? 0,
                response.LineTypeIntelligence?.ErrorCode);

            if (response.Valid != true)
            {
                return new PhoneNumberLookupResult(false, null, false);
            }

            if (response.ValidationErrors is { Count: > 0 })
            {
                return new PhoneNumberLookupResult(false, null, false);
            }

            if (!string.IsNullOrWhiteSpace(lineType) && !IsSmsCapableLineType(lineType))
            {
                return new PhoneNumberLookupResult(false, null, false);
            }

            if (string.IsNullOrWhiteSpace(response.PhoneNumber))
            {
                return new PhoneNumberLookupResult(false, null, false);
            }

            return new PhoneNumberLookupResult(true, response.PhoneNumber, false);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Twilio lookup failed with HTTP {StatusCode}.", status);
            if (status is 400 or 404)
            {
                return new PhoneNumberLookupResult(false, null, false);
            }

            return new PhoneNumberLookupResult(false, null, true);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Twilio lookup returned an unreadable response.");
            return new PhoneNumberLookupResult(false, null, true);
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Twilio lookup transport failure.");
            return new PhoneNumberLookupResult(false, null, true);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Twilio lookup timed out.");
            return new PhoneNumberLookupResult(false, null, true);
        }
    }

    public async Task<SmsSendResult> SendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using (OnceWriteDelegatingHandler.Begin())
            {
                var scheduled = request.SendAt.HasValue;
                var message = await Bounded(
                    ct => _client.Api20100401Message.CreateMessage(
                        accountSid: _options.AccountSid,
                        to: request.To,
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
                        scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                        sendAt: request.SendAt,
                        sendAsMms: null,
                        contentVariables: null,
                        riskCheck: null,
                        from: _options.FromNumber,
                        fallbackFrom: null,
                        messagingServiceSid: string.IsNullOrWhiteSpace(_options.MessagingServiceSid)
                            ? null
                            : _options.MessagingServiceSid,
                        body: request.Body,
                        mediaUrl: null,
                        contentSid: null,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);

                return ToSendResult(message, accepted: true, unknown: false);
            }
        }
        catch (DuplicateTwilioWriteException)
        {
            _logger.LogWarning("Twilio create-message duplicate write was blocked; treating outcome as unknown.");
            return new SmsSendResult(false, null, "unknown", null, null, true);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio create-message failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new SmsSendResult(false, null, "failed", null, "The provider rejected the message.", false);
        }
        catch (JsonException)
        {
            _logger.LogWarning("Twilio create-message returned an unreadable response.");
            return new SmsSendResult(false, null, "unknown", null, null, true);
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Twilio create-message transport failure.");
            return new SmsSendResult(false, null, "unknown", null, null, true);
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Twilio create-message timed out.");
            return new SmsSendResult(false, null, "unknown", null, null, true);
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.FetchMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(message);
        }
        catch (SdkException<RawError> ex) when (ex.Error.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio fetch-message failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw;
        }
        catch (JsonException)
        {
            _logger.LogWarning("Twilio fetch-message returned an unreadable response.");
            throw;
        }
    }

    public async Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (OnceWriteDelegatingHandler.Begin())
            {
                var message = await Bounded(
                    ct => _client.Api20100401Message.UpdateMessage(
                        accountSid: _options.AccountSid,
                        sid: providerSid,
                        body: null,
                        status: MessageEnumUpdateStatus.Canceled,
                        requestOptions: null,
                        ct: ct),
                    cancellationToken);
                return ToSnapshot(message);
            }
        }
        catch (DuplicateTwilioWriteException)
        {
            _logger.LogWarning("Twilio cancel duplicate write was blocked.");
            return await FetchAsync(providerSid, cancellationToken);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Twilio cancel-message failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return null;
        }
        catch (JsonException)
        {
            _logger.LogWarning("Twilio cancel-message returned an unreadable response.");
            return null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        using (OnceWriteDelegatingHandler.Begin())
        {
            var message = await Bounded(
                ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    body: "",
                    status: null,
                    requestOptions: null,
                    ct: ct),
                cancellationToken);
            return ToSnapshot(message);
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        var pages = 0;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(30));

        while (pages < MaxListPages)
        {
            pages++;
            var page = await _client.Api20100401Message.ListMessage(
                accountSid: _options.AccountSid,
                to: null,
                from: _options.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                requestOptions: null,
                ct: linked.Token);

            if (page.Messages != null)
            {
                results.AddRange(page.Messages.Select(ToSnapshot));
            }

            if (string.IsNullOrWhiteSpace(page.NextPageUri))
            {
                break;
            }

            var nextToken = ExtractPageToken(page.NextPageUri);
            if (string.IsNullOrWhiteSpace(nextToken) || string.Equals(nextToken, pageToken, StringComparison.Ordinal))
            {
                break;
            }

            pageToken = nextToken;
        }

        return results;
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(PerCallBudget);
        return await call(cts.Token);
    }

    private static SmsSendResult ToSendResult(ApiV2010AccountMessage message, bool accepted, bool unknown)
    {
        return new SmsSendResult(
            accepted,
            message.Sid,
            StatusWire(message.Status) ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            unknown);
    }

    private static SmsMessageSnapshot ToSnapshot(ApiV2010AccountMessage message)
    {
        return new SmsMessageSnapshot(
            message.Sid,
            StatusWire(message.Status),
            message.Body,
            message.From,
            message.To,
            message.DateSent,
            message.DateCreated,
            message.ErrorCode,
            message.ErrorMessage);
    }

    private static bool IsSmsCapableLineType(string lineType)
    {
        return lineType.Equals("mobile", StringComparison.OrdinalIgnoreCase)
            || lineType.Equals("nonFixedVoip", StringComparison.OrdinalIgnoreCase)
            || lineType.Equals("personal", StringComparison.OrdinalIgnoreCase)
            || lineType.Equals("fixedVoip", StringComparison.OrdinalIgnoreCase)
            || lineType.Equals("tollFree", StringComparison.OrdinalIgnoreCase);
    }

    private static string? StatusWire(MessageEnumStatus? status)
    {
        if (status is null)
        {
            return null;
        }

        if (status.Equals(MessageEnumStatus.Queued)) return "queued";
        if (status.Equals(MessageEnumStatus.Sending)) return "sending";
        if (status.Equals(MessageEnumStatus.Sent)) return "sent";
        if (status.Equals(MessageEnumStatus.Failed)) return "failed";
        if (status.Equals(MessageEnumStatus.Delivered)) return "delivered";
        if (status.Equals(MessageEnumStatus.Undelivered)) return "undelivered";
        if (status.Equals(MessageEnumStatus.Receiving)) return "receiving";
        if (status.Equals(MessageEnumStatus.Received)) return "received";
        if (status.Equals(MessageEnumStatus.Accepted)) return "accepted";
        if (status.Equals(MessageEnumStatus.Scheduled)) return "scheduled";
        if (status.Equals(MessageEnumStatus.Read)) return "read";
        if (status.Equals(MessageEnumStatus.PartiallyDelivered)) return "partially_delivered";
        if (status.Equals(MessageEnumStatus.Canceled)) return "canceled";

        return status.ToString();
    }

    private static string? ExtractPageToken(string nextPageUri)
    {
        var uri = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(nextPageUri)
            : new Uri("https://api.twilio.com" + (nextPageUri.StartsWith('/') ? nextPageUri : "/" + nextPageUri));

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(kv[1]);
            }
        }

        return null;
    }
}
