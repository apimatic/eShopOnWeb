using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 20;

    private readonly TwilioSdkClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsNotificationGateway> _logger;

    public TwilioSmsNotificationGateway(
        TwilioSdkClient client,
        IOptions<TwilioOptions> options,
        ILogger<TwilioSmsNotificationGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public string ConfiguredFromNumber => _options.FromNumber;

    public async Task<PhoneLookupResult> LookupNumberAsync(string rawNumber, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await Bounded(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: rawNumber,
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
                requestOptions: null,
                ct: ct), cancellationToken);

            var usable = lookup.Valid == true && (lookup.ValidationErrors is null || lookup.ValidationErrors.Count == 0);
            if (!usable || string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new PhoneLookupResult(false, null, "This number is not a usable destination.");
            }

            return new PhoneLookupResult(true, lookup.PhoneNumber, null);
        }
        catch (SmsProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }
    }

    public async Task<SmsMessageSnapshot> TrySendAsync(SmsSendRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceHandler.Begin())
            {
                ApiV2010AccountMessage created;
                if (request.SendAt is DateTimeOffset sendAt)
                {
                    created = await Bounded(ct => CreateMessageCore(
                        to: request.To,
                        body: request.Body,
                        from: null,
                        messagingServiceSid: _options.MessagingServiceSid,
                        scheduleType: MessageEnumScheduleType.Fixed,
                        sendAt: sendAt,
                        ct: ct), cancellationToken);
                }
                else
                {
                    created = await Bounded(ct => CreateMessageCore(
                        to: request.To,
                        body: request.Body,
                        from: _options.FromNumber,
                        messagingServiceSid: null,
                        scheduleType: null,
                        sendAt: null,
                        ct: ct), cancellationToken);
                }

                return Map(created, request.To);
            }
        }
        catch (Exception ex)
        {
            var translated = Translate(ex);
            _logger.LogWarning("Message send failed with HTTP {Status}.", translated.StatusCode);
            return new SmsMessageSnapshot(null, "failed", null, null, request.Body, null, translated.Message, null, null, null);
        }
    }

    public async Task<SmsMessageSnapshot?> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var message = await Bounded(ct => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct), cancellationToken);
            return Map(message, destinationHint: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Fetch message {Sid} failed: {ExceptionType}.", providerSid, ex.GetType().Name);
            return null;
        }
    }

    public async Task<SmsMessageSnapshot?> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            var current = await Bounded(ct => _client.Api20100401Message.FetchMessage(
                accountSid: _options.AccountSid,
                sid: providerSid,
                requestOptions: null,
                ct: ct), cancellationToken);

            if (current.Status != MessageEnumStatus.Scheduled)
            {
                return Map(current, destinationHint: null);
            }

            using (TwilioWriteOnceHandler.Begin())
            {
                var updated = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    requestOptions: null,
                    ct: ct), cancellationToken);
                return Map(updated, destinationHint: null);
            }
        }
        catch (Exception ex)
        {
            var translated = Translate(ex);
            _logger.LogWarning("Cancel scheduled message {Sid} failed with HTTP {Status}.", providerSid, translated.StatusCode);
            return null;
        }
    }

    public async Task<SmsMessageSnapshot?> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        try
        {
            using (TwilioWriteOnceHandler.Begin())
            {
                var updated = await Bounded(ct => _client.Api20100401Message.UpdateMessage(
                    accountSid: _options.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    requestOptions: null,
                    ct: ct), cancellationToken);
                return Map(updated, destinationHint: null);
            }
        }
        catch (Exception ex)
        {
            var translated = Translate(ex);
            _logger.LogWarning("Redact message {Sid} failed with HTTP {Status}.", providerSid, translated.StatusCode);
            return null;
        }
    }

    public async Task<IReadOnlyList<SmsMessageSnapshot>> ListFromConfiguredNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var results = new List<SmsMessageSnapshot>();
        string? pageToken = null;
        int? page = null;
        string? previousUri = null;

        try
        {
            for (var pages = 0; pages < MaxListPages; pages++)
            {
                var envelope = await Bounded(ct => _client.Api20100401Message.ListMessage(
                    accountSid: _options.AccountSid,
                    to: null,
                    from: _options.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: 1000,
                    page: page,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct), cancellationToken);

                if (envelope.Messages is not null)
                {
                    foreach (var message in envelope.Messages)
                    {
                        results.Add(Map(message, destinationHint: null));
                    }
                }

                if (string.IsNullOrEmpty(envelope.NextPageUri) || string.Equals(envelope.NextPageUri, previousUri, StringComparison.Ordinal))
                {
                    break;
                }

                previousUri = envelope.NextPageUri;
                pageToken = GetQueryParam(envelope.NextPageUri, "PageToken") ?? GetQueryParam(envelope.NextPageUri, "pageToken");
                var pageValue = GetQueryParam(envelope.NextPageUri, "Page") ?? GetQueryParam(envelope.NextPageUri, "page");
                page = int.TryParse(pageValue, out var parsedPage) ? parsedPage : null;
            }
        }
        catch (Exception ex)
        {
            throw Translate(ex);
        }

        return results;
    }

    private Task<ApiV2010AccountMessage> CreateMessageCore(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        return _client.Api20100401Message.CreateMessage(
            accountSid: _options.AccountSid,
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
            ct: ct);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static SmsMessageSnapshot Map(ApiV2010AccountMessage message, string? destinationHint)
    {
        var to = message.To ?? destinationHint;
        return new SmsMessageSnapshot(
            message.Sid,
            message.Status?.Value,
            to,
            message.From,
            message.Body,
            message.ErrorCode,
            Sanitize(message.ErrorMessage, to),
            message.DateSent,
            message.DateCreated,
            message.Direction?.Value);
    }

    private static string? Sanitize(string? text, string? number)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(number))
        {
            return text;
        }

        return text.Replace(number, "[redacted]", StringComparison.OrdinalIgnoreCase);
    }

    private static SmsProviderException Translate(Exception ex)
    {
        switch (ex)
        {
            case SmsProviderException already:
                return already;
            case SdkException<RawError> sdk:
                {
                    var status = (int)sdk.Error.StatusCode;
                    _ = TryReadProviderCode(sdk.Error);
                    return new SmsProviderException("The messaging provider rejected the request.", status, sdk);
                }
            case DuplicateTwilioWriteException duplicate:
                return new SmsProviderException("The messaging provider write outcome is unknown.", null, duplicate);
            case JsonException json:
                return new SmsProviderException("The provider returned a response that could not be processed.", null, json);
            case Exception transport when transport is HttpRequestException or TaskCanceledException or OperationCanceledException:
                return new SmsProviderException("The messaging provider is unreachable.", null, transport);
            default:
                return new SmsProviderException("The messaging provider is unavailable.", null, ex);
        }
    }

    private static int? TryReadProviderCode(RawError raw)
    {
        try
        {
            var body = raw.ReadAsJson<TwilioErrorBody>();
            return body?.Code;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetQueryParam(string uri, string key)
    {
        var queryIndex = uri.IndexOf('?');
        if (queryIndex < 0 || queryIndex >= uri.Length - 1)
        {
            return null;
        }

        foreach (var pair in uri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }

    private sealed class TwilioErrorBody
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("more_info")]
        public string? MoreInfo { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }
    }
}
