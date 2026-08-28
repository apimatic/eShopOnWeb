using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessageProvider : IMessageProvider
{
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);

    public TwilioMessageProvider(TwilioSdk.TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<string> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            var result = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: number,
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
                ct: ct);

            if (result.Valid != true || string.IsNullOrWhiteSpace(result.PhoneNumber))
            {
                throw new MessageProviderException("The mobile number is not a usable destination.", 422,
                    new InvalidOperationException("Provider validation rejected the destination."));
            }

            return result.PhoneNumber;
        }, cancellationToken);

    public Task<ProviderMessageSnapshot> SendAsync(string canonicalNumber, string body, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            using var writeScope = SingleProviderWriteHandler.BeginScope();
            var message = await CreateMessageAsync(canonicalNumber, body, null, null, null, ct);
            return ToSnapshot(message);
        }, cancellationToken);

    public Task<ProviderMessageSnapshot> ScheduleAsync(string canonicalNumber, string body,
        DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            using var writeScope = SingleProviderWriteHandler.BeginScope();
            var message = await CreateMessageAsync(canonicalNumber, body,
                MessageEnumScheduleType.Fixed, sendAt, _settings.MessagingServiceSid, ct);
            return ToSnapshot(message);
        }, cancellationToken);

    public Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        BoundedAsync(async ct => ToSnapshot(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        BoundedAsync(async ct => ToSnapshot(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public Task<ProviderMessageSnapshot> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        BoundedAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct);
            var refreshed = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                requestOptions: null,
                ct: ct);
            if (!string.IsNullOrEmpty(refreshed.Body))
            {
                throw new MessageProviderException("The provider did not confirm content disposal.", 502,
                    new InvalidOperationException("Provider content remains present."));
            }
            return ToSnapshot(refreshed);
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken) => BoundedAsync<IReadOnlyList<ProviderMessageRecord>>(async ct =>
    {
        var records = new List<ProviderMessageRecord>();
        string? pageToken = null;
        string? previousToken = null;
        const int maxPages = 10000;

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to.UtcDateTime.Date.AddDays(1),
                dateSentQueryQuery: from.UtcDateTime.Date.AddDays(-1),
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                requestOptions: null,
                ct: ct);

            foreach (var message in response.Messages ?? Array.Empty<ApiV2010AccountMessage>())
            {
                var dateSent = ParseProviderTimestamp(message.DateSent);
                if (string.IsNullOrWhiteSpace(message.Sid))
                    throw new MessageProviderException("The provider returned a message without an identifier.", 502,
                        new InvalidOperationException("A reconciliation record has no provider SID."));
                if (dateSent is not null && (dateSent < from || dateSent > to)) continue;
                records.Add(ToRecord(message, dateSent));
            }

            if (string.IsNullOrWhiteSpace(response.NextPageUri)) return records;
            previousToken = pageToken;
            pageToken = ReadQueryValue(response.NextPageUri, "PageToken");
            if (string.IsNullOrWhiteSpace(pageToken) || pageToken == previousToken)
            {
                throw new MessageProviderException("Provider pagination did not make progress.", 502,
                    new InvalidOperationException("Missing or repeated page token."));
            }
        }

        throw new MessageProviderException("The reconciliation range exceeded the safe provider page limit.", 502,
            new InvalidOperationException("Provider pagination exceeded 10000 pages."));
    }, cancellationToken);

    private Task<ApiV2010AccountMessage> CreateMessageAsync(string to, string body,
        MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, string? messagingServiceSid,
        CancellationToken cancellationToken) => _client.Api20100401Message.CreateMessage(
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
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: messagingServiceSid,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: cancellationToken);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        try
        {
            return await call(deadline.Token);
        }
        catch (MessageProviderException) { throw; }
        catch (SdkException<RawError> ex)
        {
            throw new MessageProviderException("The messaging provider rejected the request.",
                (int)ex.Error.StatusCode, ex);
        }
        catch (DuplicateProviderWriteBlockedException ex)
        {
            throw new MessageProviderException("The provider write outcome is unknown; an automatic duplicate was blocked.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MessageProviderException("The messaging provider returned an unreadable response.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessageProviderException("The messaging provider is unavailable.",
                ex is TaskCanceledException ? (int)HttpStatusCode.GatewayTimeout : null, ex);
        }
    }

    private static ProviderMessageSnapshot ToSnapshot(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new MessageProviderException("The messaging provider returned no message identifier.", 502,
                new InvalidOperationException("Missing provider message SID."));
        }

        return new ProviderMessageSnapshot(message.Sid, message.Status?.Value,
            message.ErrorCode is null ? null : Convert.ToInt32(message.ErrorCode), null,
            ParseProviderTimestamp(message.DateCreated), ParseProviderTimestamp(message.DateSent), message.Body);
    }

    private static ProviderMessageRecord ToRecord(ApiV2010AccountMessage message, DateTimeOffset? dateSent = null) =>
        new(message.Sid!, message.Status?.Value,
            message.ErrorCode is null ? null : Convert.ToInt32(message.ErrorCode), null,
            ParseProviderTimestamp(message.DateCreated), dateSent ?? ParseProviderTimestamp(message.DateSent));

    private static DateTimeOffset? ParseProviderTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadQueryValue(string uri, string name)
    {
        var queryIndex = uri.IndexOf('?');
        if (queryIndex < 0 || queryIndex == uri.Length - 1) return null;
        foreach (var pair in uri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }
}
