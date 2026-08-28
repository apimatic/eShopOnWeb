using System;
using System.Collections.Generic;
using System.Linq;
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

namespace Microsoft.eShopWeb.PublicApi.Notifications;

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private const int PageSize = 1000;
    private const int MaxPages = 1000;

    private readonly TwilioSdk.TwilioSdkClient _client;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(TwilioSdk.TwilioSdkClient client, IOptions<TwilioSettings> settings)
    {
        _client = client;
        _settings = settings.Value;
    }

    public string ConfiguredFromNumber => _settings.FromNumber;

    public Task<string> ValidateAndCanonicalizeAsync(string suppliedNumber, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct =>
        {
            var result = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: suppliedNumber,
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
                throw new InvalidContactNumberException();
            }

            return result.PhoneNumber;
        }, cancellationToken);

    public Task<ProviderMessage> SendAsync(
        string canonicalNumber,
        string content,
        DateTimeOffset? scheduledFor,
        CancellationToken cancellationToken) =>
        ExecuteWriteAsync(async ct =>
        {
            var message = await _client.Api20100401Message.CreateMessage(
                accountSid: _settings.AccountSid,
                to: canonicalNumber,
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
                scheduleType: scheduledFor.HasValue ? MessageEnumScheduleType.Fixed : null,
                sendAt: scheduledFor,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: scheduledFor.HasValue ? _settings.MessagingServiceSid : null,
                body: content,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct);

            return MapRequired(message);
        }, cancellationToken);

    public Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken cancellationToken) =>
        ExecuteAsync(async ct => MapRequired(await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public Task<ProviderMessage> CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken) =>
        ExecuteWriteAsync(async ct => MapRequired(await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null,
            ct: ct)), cancellationToken);

    public async Task<ProviderMessage> RedactContentAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        await ExecuteWriteAsync(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: string.Empty,
                status: null,
                requestOptions: null,
                ct: ct);
            return true;
        }, cancellationToken);

        return await ExecuteAsync(async ct =>
        {
            var fetched = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                requestOptions: null,
                ct: ct);

            if (!string.IsNullOrEmpty(fetched.Body))
            {
                throw new TwilioProviderException("The provider did not confirm content disposal.");
            }

            return MapRequired(fetched);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<ProviderMessage>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<ProviderMessage>>(async ct =>
        {
            var messages = new List<ProviderMessage>();
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? pageToken = null;

            for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
            {
                var page = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: PageSize,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct);

                messages.AddRange((page.Messages ?? Array.Empty<ApiV2010AccountMessage>()).Select(MapRequired));

                if (string.IsNullOrWhiteSpace(page.NextPageUri))
                {
                    return messages;
                }

                pageToken = ExtractPageToken(page.NextPageUri);
                if (string.IsNullOrWhiteSpace(pageToken) || !seenTokens.Add(pageToken))
                {
                    throw new TwilioProviderException("The provider returned invalid or non-advancing pagination metadata.");
                }
            }

            throw new TwilioProviderException("The reconciliation page limit was reached; no partial report was returned.");
        }, cancellationToken);

    private static string? ExtractPageToken(string nextPageUri)
    {
        if (!Uri.TryCreate(nextPageUri, UriKind.RelativeOrAbsolute, out var uri)) return null;
        var query = uri.IsAbsoluteUri ? uri.Query : nextPageUri[(nextPageUri.IndexOf('?') + 1)..];
        return QueryHelpers.ParseQuery(query).TryGetValue("PageToken", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static ProviderMessage MapRequired(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new TwilioProviderException("The provider response did not contain a message identifier.");
        }

        return new ProviderMessage(
            message.Sid,
            message.Status?.Value,
            message.ErrorCode,
            message.ErrorMessage,
            message.DateCreated,
            message.DateUpdated,
            message.DateSent,
            message.Direction?.Value,
            message.From,
            message.To,
            message.Body);
    }

    private static async Task<T> ExecuteWriteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var scope = TwilioWriteGuardHandler.BeginWrite();
        return await ExecuteAsync(call, cancellationToken);
    }

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(CallBudget);

        try
        {
            return await call(deadline.Token);
        }
        catch (InvalidContactNumberException)
        {
            throw;
        }
        catch (TwilioProviderException)
        {
            throw;
        }
        catch (SdkException<RawError> ex)
        {
            throw new TwilioProviderException("The provider rejected the request.", ex.Error.StatusCode, ex);
        }
        catch (TwilioWriteOutcomeUnknownException ex)
        {
            throw new TwilioProviderException("The provider write outcome is unknown and requires reconciliation.", null, ex);
        }
        catch (JsonException ex)
        {
            throw new TwilioProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new TwilioProviderException("The provider is unreachable.", null, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TwilioProviderException("The provider request timed out.", null, ex);
        }
    }
}
