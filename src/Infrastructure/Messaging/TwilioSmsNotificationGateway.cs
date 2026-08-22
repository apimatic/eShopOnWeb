using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioSmsNotificationGateway : ISmsNotificationGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 20;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsNotificationGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<PhoneLookupResult> LookupDestinationAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return Execute(async ct =>
        {
            var lookup = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
                ct: ct);

            var lineType = lookup.LineTypeIntelligence?.Type;
            var lineTypeError = lookup.LineTypeIntelligence?.ErrorCode;
            var usable = PhoneDestinationRules.IsUsableSmsDestination(lookup.Valid, lineType, lineTypeError);

            if (!usable)
            {
                var reason = lookup.Valid != true
                    ? "This number is not a usable SMS destination."
                    : $"This number is not a usable SMS destination (line type '{lineType}').";
                return new PhoneLookupResult(false, lookup.PhoneNumber, reason);
            }

            if (string.IsNullOrWhiteSpace(lookup.PhoneNumber))
            {
                return new PhoneLookupResult(false, null, "This number is not a usable SMS destination.");
            }

            return new PhoneLookupResult(true, lookup.PhoneNumber, null);
        }, cancellationToken);
    }

    public Task<ProviderMessageSnapshot> SendImmediatelyAsync(string to, string body, CancellationToken cancellationToken)
    {
        return Execute(ct => CreateMessageAsync(
            to: to,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: null,
            scheduleType: null,
            sendAt: null,
            ct: ct), cancellationToken);
    }

    public Task<ProviderMessageSnapshot> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        return Execute(ct => CreateMessageAsync(
            to: to,
            body: body,
            from: _settings.FromNumber,
            messagingServiceSid: _settings.MessagingServiceSid,
            scheduleType: MessageEnumScheduleType.Fixed,
            sendAt: sendAt,
            ct: ct), cancellationToken);
    }

    public Task<ProviderMessageSnapshot> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        return Execute(async ct =>
        {
            using (SmsWriteOnceHandler.BeginWrite())
            {
                var updated = await _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: null,
                    status: MessageEnumUpdateStatus.Canceled,
                    ct: ct);
                return Map(updated);
            }
        }, cancellationToken);
    }

    public Task<ProviderMessageSnapshot> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        return Execute(async ct =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerSid,
                ct: ct);
            return Map(message);
        }, cancellationToken);
    }

    public Task<ProviderMessageSnapshot> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        return Execute(async ct =>
        {
            using (SmsWriteOnceHandler.BeginWrite())
            {
                var updated = await _client.Api20100401Message.UpdateMessage(
                    accountSid: _settings.AccountSid,
                    sid: providerSid,
                    body: string.Empty,
                    status: null,
                    ct: ct);
                return Map(updated);
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ProviderMessageSnapshot>> ListSentFromConfiguredNumberAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toInclusive,
        CancellationToken cancellationToken)
    {
        return Execute(async ct =>
        {
            var after = fromInclusive.ToUniversalTime().AddSeconds(-1);
            var before = toInclusive.ToUniversalTime().AddSeconds(1);
            var results = new List<ProviderMessageSnapshot>();
            string? pageToken = null;
            var pages = 0;
            var truncated = false;

            while (pages < MaxListPages)
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: before,
                    dateSentQueryQuery: after,
                    pageSize: 1000,
                    page: null,
                    pageToken: pageToken,
                    ct: ct);

                if (page.Messages is not null)
                {
                    foreach (var message in page.Messages)
                    {
                        results.Add(Map(message));
                    }
                }

                pages++;

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                {
                    break;
                }

                var nextToken = PageTokenFrom(page.NextPageUri);
                if (string.IsNullOrWhiteSpace(nextToken) || string.Equals(nextToken, pageToken, StringComparison.Ordinal))
                {
                    break;
                }

                if (pages >= MaxListPages)
                {
                    truncated = true;
                    break;
                }

                pageToken = nextToken;
            }

            if (truncated)
            {
                throw new SmsProviderException("The reconciliation window exceeded the page cap.");
            }

            return (IReadOnlyList<ProviderMessageSnapshot>)results;
        }, cancellationToken);
    }

    private async Task<ProviderMessageSnapshot> CreateMessageAsync(
        string to,
        string body,
        string? from,
        string? messagingServiceSid,
        MessageEnumScheduleType? scheduleType,
        DateTimeOffset? sendAt,
        CancellationToken ct)
    {
        using (SmsWriteOnceHandler.BeginWrite())
        {
            var created = await _client.Api20100401Message.CreateMessage(
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
                ct: ct);
            return Map(created);
        }
    }

    private static ProviderMessageSnapshot Map(ApiV2010AccountMessage message)
    {
        return new ProviderMessageSnapshot(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.Body,
            message.DateSent,
            message.DateCreated,
            message.From,
            message.To,
            message.MessagingServiceSid);
    }

    private static string? PageTokenFrom(string nextPageUri)
    {
        if (!Uri.TryCreate(nextPageUri, UriKind.Absolute, out var uri)
            && !Uri.TryCreate("https://api.twilio.com" + nextPageUri, UriKind.Absolute, out uri))
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
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private async Task<T> Execute<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException("The messaging provider rejected the request.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new SmsProviderException("The messaging provider returned a response that could not be processed.", innerException: ex);
        }
        catch (DuplicateProviderWriteException ex)
        {
            throw new SmsProviderException("The messaging provider write may already have been accepted.", innerException: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new SmsProviderException("The messaging provider is unreachable.", innerException: ex);
        }
    }
}
