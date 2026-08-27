using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.OrderNotifications;

public sealed class TwilioTextMessageProvider : ITextMessageProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(15);
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioTextMessageProvider(TwilioSdk.TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<string?> ValidateAndCanonicalizeAsync(string number, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(token => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
            ct: token), false, cancellationToken);

        return response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber)
            ? response.PhoneNumber
            : null;
    }

    public Task<ProviderMessageSnapshot> SendAsync(string destination, string body, CancellationToken cancellationToken) =>
        CreateAsync(destination, body, null, cancellationToken);

    public Task<ProviderMessageSnapshot> ScheduleAsync(string destination, string body, DateTimeOffset sendAt, CancellationToken cancellationToken) =>
        CreateAsync(destination, body, sendAt, cancellationToken);

    public async Task<ProviderMessageSnapshot> CancelAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var message = await ExecuteAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: token), true, cancellationToken);
        return Map(message);
    }

    public async Task<ProviderMessageSnapshot> FetchAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var message = await ExecuteAsync(token => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            requestOptions: null,
            ct: token), false, cancellationToken);
        return Map(message);
    }

    public async Task<ProviderMessageSnapshot> RedactAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        await ExecuteAsync(token => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: string.Empty,
            status: null,
            requestOptions: null,
            ct: token), true, cancellationToken);

        var confirmed = await FetchAsync(providerMessageSid, cancellationToken);
        if (!string.IsNullOrEmpty(confirmed.Body))
        {
            throw new MessagingProviderException("The provider did not confirm content disposal.", 502);
        }

        return confirmed;
    }

    public async Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const int maxPages = 10_000;
        const long pageSize = 1000;
        var messages = new List<ProviderMessageSnapshot>();
        string? pageToken = null;
        string? previousToken = null;

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            var response = await ExecuteAsync(token => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: pageSize,
                page: null,
                pageToken: pageToken,
                requestOptions: null,
                ct: token), false, cancellationToken);

            if (response.Messages is not null)
            {
                foreach (var message in response.Messages)
                {
                    var snapshot = Map(message);
                    if (IsWithinRange(snapshot, from, to))
                    {
                        messages.Add(snapshot);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(response.NextPageUri))
            {
                return messages;
            }

            var nextUri = new Uri(new Uri("https://placeholder.invalid"), response.NextPageUri);
            var query = QueryHelpers.ParseQuery(nextUri.Query);
            if (!query.TryGetValue("PageToken", out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            {
                throw new MessagingProviderException("The provider returned invalid paging metadata.", 502);
            }

            pageToken = values[0]!;
            if (string.Equals(pageToken, previousToken, StringComparison.Ordinal))
            {
                throw new MessagingProviderException("The provider paging cursor did not advance.", 502);
            }
            previousToken = pageToken;
        }

        throw new MessagingProviderException("The reconciliation range exceeded the provider paging safety limit.", 502);
    }

    private async Task<ProviderMessageSnapshot> CreateAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var message = await ExecuteAsync(token => _client.Api20100401Message.CreateMessage(
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
            messagingServiceSid: sendAt.HasValue ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            requestOptions: null,
            ct: token), true, cancellationToken);

        return Map(message);
    }

    private static ProviderMessageSnapshot Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value,
        message.Direction?.Value,
        message.Body,
        message.ErrorCode,
        message.ErrorMessage,
        message.DateCreated,
        message.DateSent,
        message.DateUpdated);

    private static bool IsWithinRange(ProviderMessageSnapshot message, DateTimeOffset from, DateTimeOffset to)
    {
        var value = message.DateSent ?? message.DateCreated;
        return value is not null && DateTimeOffset.TryParse(value, out var timestamp) && timestamp >= from && timestamp <= to;
    }

    private static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        bool isWrite,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);

        try
        {
            using var writeScope = isWrite ? ProviderWriteScope.Begin() : null;
            return await operation(deadline.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            var safeMessage = status is 401 or 403
                ? "The messaging provider is unavailable."
                : status == 429
                    ? "The messaging provider is temporarily unavailable."
                    : "The messaging provider rejected the request.";
            throw new MessagingProviderException(safeMessage, status, ex);
        }
        catch (DuplicateProviderWritePreventedException ex)
        {
            throw new MessagingProviderException("The provider write outcome is unknown; an automatic duplicate was prevented.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The messaging provider returned an unreadable response.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider could not be reached.", null, ex);
        }
    }
}
