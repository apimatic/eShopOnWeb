using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal sealed class TwilioMessagingProvider(
    TwilioSdkClient client,
    IOptions<TwilioSettings> options,
    SingleProviderWriteScope writeScope) : IMessagingProvider
{
    private readonly TwilioSettings _settings = options.Value;

    public async Task<DestinationValidation> ValidateDestinationAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: input,
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
                ct: cancellationToken);

            return new DestinationValidation(result.Valid is true && !string.IsNullOrWhiteSpace(result.PhoneNumber), result.PhoneNumber);
        }
        catch (Exception ex)
        {
            throw Translate(ex, "The messaging provider could not validate the destination.");
        }
    }

    public async Task<ProviderMessageState> SendAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = writeScope.Begin();
            var result = await client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: destination,
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
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: cancellationToken);

            var state = Map(result);
            if (sendAt.HasValue && !string.Equals(state.From, _settings.FromNumber, StringComparison.Ordinal))
            {
                throw new MessagingProviderException("The provider scheduled the message under an unexpected sender.");
            }

            return state;
        }
        catch (MessagingProviderException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw Translate(ex, "The messaging provider could not accept the message.");
        }
    }

    public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: cancellationToken),
            "The messaging provider could not return the message.");

    public Task<ProviderMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(
            () => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                ct: cancellationToken),
            "The messaging provider could not cancel the scheduled message.");

    public async Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        await ExecuteWriteAsync(
            () => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: cancellationToken),
            "The messaging provider could not dispose of the message content.");

        var verified = await FetchAsync(providerMessageSid, cancellationToken);
        if (!string.IsNullOrEmpty(verified.Body))
        {
            throw new MessagingProviderException("The messaging provider did not confirm content disposal.");
        }

        return verified;
    }

    public async Task<IReadOnlyList<ProviderMessageState>> ListSentAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const int maxPages = 1000;
        var results = new List<ProviderMessageState>();
        string? pageToken = null;
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var lowerDay = new DateTimeOffset(from.UtcDateTime.Date, TimeSpan.Zero).AddDays(-1);
        var upperDay = new DateTimeOffset(to.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            ListMessageResponse page;
            try
            {
                page = await client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: upperDay,
                    dateSentQueryQuery: lowerDay,
                    pageSize: 1000,
                    page: null,
                    pageToken: pageToken,
                    ct: cancellationToken);
            }
            catch (Exception ex)
            {
                throw Translate(ex, "The messaging provider could not complete reconciliation.");
            }

            foreach (var message in page.Messages ?? Array.Empty<ApiV2010AccountMessage>())
            {
                var state = Map(message);
                if (TryParseProviderDate(state.DateSent, out var sent) && sent >= from && sent <= to)
                {
                    results.Add(state);
                }
            }

            if (string.IsNullOrWhiteSpace(page.NextPageUri))
            {
                return results;
            }

            var nextToken = ReadPageToken(page.NextPageUri);
            if (string.IsNullOrWhiteSpace(nextToken) || !seenTokens.Add(nextToken))
            {
                throw new MessagingProviderException("The messaging provider returned an invalid pagination sequence.");
            }

            pageToken = nextToken;
        }

        throw new MessagingProviderException("The reconciliation range exceeded the safe pagination limit.");
    }

    private async Task<ProviderMessageState> ExecuteAsync(
        Func<Task<ApiV2010AccountMessage>> action,
        string safeMessage)
    {
        try
        {
            return Map(await action());
        }
        catch (Exception ex)
        {
            throw Translate(ex, safeMessage);
        }
    }

    private async Task<ProviderMessageState> ExecuteWriteAsync(
        Func<Task<ApiV2010AccountMessage>> action,
        string safeMessage)
    {
        try
        {
            using var scope = writeScope.Begin();
            return Map(await action());
        }
        catch (Exception ex)
        {
            throw Translate(ex, safeMessage);
        }
    }

    private static ProviderMessageState Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value,
        message.From,
        message.MessagingServiceSid,
        message.DateCreated,
        message.DateSent,
        message.DateUpdated,
        message.ErrorCode,
        message.ErrorMessage,
        message.Body);

    private static MessagingProviderException Translate(Exception ex, string safeMessage) => ex switch
    {
        SdkException<RawError> sdk when (int)sdk.Error.StatusCode is 401 or 403 =>
            new MessagingProviderException("The messaging provider is unavailable.", 502, sdk),
        SdkException<RawError> sdk when (int)sdk.Error.StatusCode == 429 =>
            new MessagingProviderException("The messaging provider is temporarily unavailable.", 503, sdk),
        SdkException<RawError> sdk =>
            new MessagingProviderException(safeMessage, (int)sdk.Error.StatusCode, sdk),
        DuplicateProviderWriteAttemptException duplicate =>
            new MessagingProviderException("The provider write has an unknown outcome and was not duplicated.", null, duplicate),
        HttpRequestException or TaskCanceledException =>
            new MessagingProviderException("The messaging provider is unreachable.", null, ex),
        JsonException =>
            new MessagingProviderException("The messaging provider returned a response that could not be processed.", null, ex),
        _ => new MessagingProviderException(safeMessage, null, ex)
    };

    private static bool TryParseProviderDate(string? value, out DateTimeOffset parsed) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed);

    private static string? ReadPageToken(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = pair.Split('=', 2);
            if (pieces.Length == 2 && string.Equals(Uri.UnescapeDataString(pieces[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pieces[1]);
            }
        }

        return null;
    }
}
