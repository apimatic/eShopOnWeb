using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingGateway(
    TwilioSdk.TwilioSdkClient client,
    IOptions<TwilioSettings> settings,
    TwilioWriteGuard writeGuard) : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSettings _settings = settings.Value;

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string submittedNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(ct => client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: submittedNumber,
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

            return new PhoneValidationResult(response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber),
                response.Valid == true ? response.PhoneNumber : null);
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("The phone number could not be validated by the messaging provider.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw ProviderFailure("The phone number validation service is unavailable.", ex);
        }
    }

    public async Task<ProviderMessageResult> SendMessageAsync(string destination, string body,
        DateTimeOffset? scheduledFor, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = writeGuard.Begin();
            var response = await BoundedAsync(ct => client.Api20100401Message.CreateMessage(
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
                scheduleType: scheduledFor is null ? null : MessageEnumScheduleType.Fixed,
                sendAt: scheduledFor?.ToUniversalTime(),
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct), cancellationToken);

            var snapshot = ToSnapshot(response);
            return string.IsNullOrWhiteSpace(snapshot.ProviderMessageSid)
                ? new ProviderMessageResult(false, null, "Unknown")
                : new ProviderMessageResult(true, snapshot, snapshot.Status);
        }
        catch (Exception ex) when (IsProviderFailure(ex))
        {
            return new ProviderMessageResult(false, null,
                ex is TwilioDuplicateWriteBlockedException ? "Unknown" : "Failed");
        }
    }

    public Task<ProviderMessageSnapshot> FetchMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken) => ReadAsync(
            ct => client.Api20100401Message.FetchMessage(_settings.AccountSid, providerMessageSid, null, ct),
            "The provider message could not be read.", cancellationToken);

    public Task<ProviderMessageSnapshot> CancelScheduledMessageAsync(string providerMessageSid,
        CancellationToken cancellationToken) => WriteAsync(
            ct => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct),
            "The scheduled provider message could not be cancelled.", cancellationToken);

    public Task<ProviderMessageSnapshot> DisposeMessageContentAsync(string providerMessageSid,
        CancellationToken cancellationToken) => WriteAsync(
            ct => client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct),
            "The provider message content could not be disposed.", cancellationToken);

    public async Task<IReadOnlyList<ProviderMessageSnapshot>> ListMessagesAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<ProviderMessageSnapshot>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? pageToken = null;
            var lowerBound = new DateTimeOffset(from.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
            var upperBound = new DateTimeOffset(to.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

            for (var pageCount = 0; pageCount < 1000; pageCount++)
            {
                var response = await BoundedAsync(ct => client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: upperBound,
                    dateSentQueryQuery: lowerBound,
                    pageSize: 1000,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct), cancellationToken);

                foreach (var message in response.Messages ?? Array.Empty<ApiV2010AccountMessage>())
                {
                    var snapshot = ToSnapshot(message);
                    var instant = snapshot.DateSent ?? snapshot.DateCreated;
                    if (instant >= from && instant <= to)
                    {
                        results.Add(snapshot);
                    }
                }

                var next = ExtractPageToken(response.NextPageUri);
                if (next is null)
                {
                    return results;
                }

                if (!seenTokens.Add(next))
                {
                    throw new MessagingProviderException("The provider returned a non-advancing reconciliation cursor.");
                }

                pageToken = next;
            }

            throw new MessagingProviderException("The reconciliation range exceeded the safe provider page limit.");
        }
        catch (MessagingProviderException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure("The provider reconciliation query failed.", ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw ProviderFailure("The provider reconciliation query could not be completed.", ex);
        }
    }

    private async Task<ProviderMessageSnapshot> ReadAsync(
        Func<CancellationToken, Task<ApiV2010AccountMessage>> call,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            return ToSnapshot(await BoundedAsync(call, cancellationToken));
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(failureMessage, ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw ProviderFailure(failureMessage, ex);
        }
    }

    private async Task<ProviderMessageSnapshot> WriteAsync(
        Func<CancellationToken, Task<ApiV2010AccountMessage>> call,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = writeGuard.Begin();
            return ToSnapshot(await BoundedAsync(call, cancellationToken));
        }
        catch (SdkException<RawError> ex)
        {
            throw ProviderFailure(failureMessage, ex);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        {
            throw ProviderFailure(failureMessage, ex);
        }
    }

    private static async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        return await call(deadline.Token);
    }

    private static ProviderMessageSnapshot ToSnapshot(ApiV2010AccountMessage message) => new(
        message.Sid ?? string.Empty,
        message.Status?.Value ?? "Unknown",
        message.Body,
        message.ErrorCode,
        ParseDate(message.DateCreated),
        ParseDate(message.DateSent),
        ParseDate(message.DateUpdated),
        message.Direction?.Value);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri)) return null;
        var queryStart = nextPageUri.IndexOf('?');
        if (queryStart < 0) return null;
        foreach (var pair in nextPageUri[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), "PageToken",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1].Replace('+', ' '));
            }
        }
        return null;
    }

    private static MessagingProviderException ProviderFailure(string message, SdkException<RawError> ex) =>
        new(message, (int)ex.Error.StatusCode, ex);

    private static MessagingProviderException ProviderFailure(string message, Exception ex) => new(message, null, ex);

    private static bool IsBoundaryFailure(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or TwilioDuplicateWriteBlockedException;

    private static bool IsProviderFailure(Exception ex) =>
        ex is SdkException<RawError> || IsBoundaryFailure(ex);
}
