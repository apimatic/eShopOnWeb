using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan TotalCallBudget = TimeSpan.FromSeconds(15);
    private const int MaximumPages = 100;
    private const long PageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public Task<PhoneValidationResult> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            try
            {
                var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                    phoneNumber: phoneNumber,
                    fields: "validation",
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

                return response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber)
                    ? new PhoneValidationResult(true, response.PhoneNumber)
                    : new PhoneValidationResult(false, null);
            }
            catch (SdkException<RawError> ex) when (ex.Error.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity)
            {
                return new PhoneValidationResult(false, null);
            }
        }, cancellationToken);

    public Task<ProviderMessage> SendAsync(string destination, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
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
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: ct);

            return Map(response);
        }, cancellationToken);

    public Task<ProviderMessage> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct => Map(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> CancelAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct => Map(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageSid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                body: string.Empty,
                status: null,
                ct: ct);

            var fetched = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageSid,
                ct: ct);

            if (!string.IsNullOrEmpty(fetched.Body))
            {
                throw new TwilioProviderException("The provider did not confirm content disposal.", null, new InvalidOperationException());
            }

            return Map(fetched);
        }, cancellationToken);

    public Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            var results = new List<ProviderMessage>();
            string? pageToken = null;

            var providerLowerBound = new DateTimeOffset(from.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
            var providerUpperBound = new DateTimeOffset(to.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);

            for (var pageNumber = 0; pageNumber < MaximumPages; pageNumber++)
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: providerUpperBound,
                    dateSentQueryQuery: providerLowerBound,
                    pageSize: PageSize,
                    page: null,
                    pageToken: pageToken,
                    ct: ct);

                results.AddRange((page.Messages ?? Array.Empty<ApiV2010AccountMessage>())
                    .Select(Map)
                    .Where(message => IsInRange(message, from, to)));

                var nextToken = GetPageToken(page.NextPageUri);
                if (nextToken is null)
                {
                    return (IReadOnlyList<ProviderMessage>)results;
                }

                if (string.Equals(nextToken, pageToken, StringComparison.Ordinal))
                {
                    throw new TwilioProviderException("The provider returned a non-advancing page token.", null, new InvalidOperationException());
                }

                pageToken = nextToken;
            }

            throw new TwilioProviderException("The provider message listing exceeded the safety page limit.", null, new InvalidOperationException());
        }, cancellationToken);

    private static bool IsInRange(ProviderMessage message, DateTimeOffset from, DateTimeOffset to)
    {
        var timestamp = message.DateSent ?? message.DateCreated;
        return timestamp.HasValue && timestamp.Value >= from && timestamp.Value <= to;
    }

    private static string? GetPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var questionMark = nextPageUri.IndexOf('?', StringComparison.Ordinal);
        if (questionMark < 0 || questionMark == nextPageUri.Length - 1)
        {
            return null;
        }

        foreach (var pair in nextPageUri[(questionMark + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            var name = separator < 0 ? pair : pair[..separator];
            if (string.Equals(Uri.UnescapeDataString(name), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }
        }

        return null;
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new JsonException("The provider response omitted the message identifier.");
        }

        return new ProviderMessage(
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            message.ErrorMessage,
            message.From,
            message.To,
            message.Body,
            message.MessagingServiceSid,
            ParseProviderDate(message.DateCreated),
            ParseProviderDate(message.DateSent),
            ParseProviderDate(message.DateUpdated));
    }

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var result)
            ? result.ToUniversalTime()
            : null;

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TotalCallBudget);

        try
        {
            return await call(deadline.Token);
        }
        catch (TwilioProviderException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw new TwilioProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            throw new TwilioProviderException("The messaging provider could not complete the request.", null, ex);
        }
    }
}
