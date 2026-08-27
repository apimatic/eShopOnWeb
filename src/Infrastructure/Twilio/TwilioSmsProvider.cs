using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

public sealed class TwilioSmsProvider : ISmsProvider
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioSmsProvider(TwilioSdk.TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public async Task<PhoneValidationResult> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(ct => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
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
            requestOptions: null,
            ct: ct), cancellationToken);

        var errors = response.ValidationErrors?.Select(x => x.Value).ToArray() ?? Array.Empty<string>();
        var valid = response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber);
        return new PhoneValidationResult(valid, valid ? response.PhoneNumber : null, errors);
    }

    public Task<ProviderMessageSnapshot> SendAsync(
        string destination,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(async ct =>
        {
            var response = await _client.Api20100401Message.CreateMessage(
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
                body: content,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct);

            var snapshot = Map(response);
            if (string.IsNullOrWhiteSpace(snapshot.ProviderMessageId))
            {
                throw new SmsProviderException("The messaging provider returned an unusable response.");
            }

            return snapshot;
        }, cancellationToken);
    }

    public Task<ProviderMessageSnapshot> CancelAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(async ct =>
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct);
            return Map(response);
        }, cancellationToken);
    }

    public async Task<ProviderMessageSnapshot> FetchAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        var response = await ExecuteAsync(ct => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            requestOptions: null,
            ct: ct), cancellationToken);
        return Map(response);
    }

    public Task<ProviderMessageSnapshot> DisposeContentAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        return ExecuteWriteAsync(async ct =>
        {
            var response = await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct);
            var snapshot = Map(response);
            if (!string.IsNullOrEmpty(snapshot.Body))
            {
                throw new SmsProviderException("The messaging provider did not confirm content disposal.");
            }

            return snapshot;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        const int maxPages = 10000;
        var messages = new List<ProviderMessageSnapshot>();
        string? pageToken = null;
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);

        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            var response = await ExecuteAsync(ct => _client.Api20100401Message.ListMessage(
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
                ct: ct), cancellationToken);

            if (response.Messages is not null)
            {
                messages.AddRange(response.Messages.Select(Map));
            }

            if (string.IsNullOrWhiteSpace(response.NextPageUri))
            {
                return messages;
            }

            pageToken = ExtractPageToken(response.NextPageUri);
            if (string.IsNullOrWhiteSpace(pageToken) || !seenTokens.Add(pageToken))
            {
                throw new SmsProviderException("The messaging provider returned invalid pagination state.");
            }
        }

        throw new SmsProviderException("The messaging provider response exceeded the reconciliation safety limit.");
    }

    private static string? ExtractPageToken(string nextPageUri)
    {
        var queryIndex = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0 || queryIndex == nextPageUri.Length - 1)
        {
            return null;
        }

        foreach (var part in nextPageUri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static ProviderMessageSnapshot Map(ApiV2010AccountMessage message) => new(
        message.Sid,
        message.Status?.Value,
        message.ErrorCode,
        message.ErrorMessage,
        ParseDate(message.DateCreated),
        ParseDate(message.DateSent),
        ParseDate(message.DateUpdated),
        message.From,
        message.To,
        message.Body,
        message.MessagingServiceSid);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);
        try
        {
            return await operation(deadline.Token);
        }
        catch (SdkException<RawError> exception)
        {
            throw new SmsProviderException("The messaging provider rejected the request.", exception.Error.StatusCode, exception);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unavailable.", null, exception);
        }
        catch (JsonException exception)
        {
            throw new SmsProviderException("The messaging provider returned a response that could not be processed.", null, exception);
        }
    }

    private static async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var scope = TwilioWriteOnceHandler.BeginScope();
        try
        {
            return await ExecuteAsync(operation, cancellationToken);
        }
        catch (TwilioWriteRetryBlockedException exception)
        {
            throw new SmsProviderException("The messaging provider write has an unknown outcome.", null, exception);
        }
    }
}
