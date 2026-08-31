using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Messaging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Twilio-backed implementation of the messaging boundary. Every call runs inside one
/// deadline and one error ladder; provider failures leave this class only as
/// <see cref="MessagingProviderException"/> with caller-safe messages (never a provider
/// body, never a destination number, never credentials).
/// </summary>
public class TwilioTextMessagingService : ITextMessagingService
{
    public const string HttpClientName = "Twilio";

    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioTextMessagingService> _logger;

    public TwilioTextMessagingService(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        IAppLogger<TwilioTextMessagingService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ValidatedPhoneNumber> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var response = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber: phoneNumber,
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

            var isValid = response.Valid == true;
            var errors = response.ValidationErrors?
                .Select(e => e.Value)
                .ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();

            return new ValidatedPhoneNumber(isValid, isValid ? response.PhoneNumber : null, errors);
        }, cancellationToken);
    }

    public async Task<SentTextMessage> SendAsync(string to, string body, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            using var scope = SendOnceGuardHandler.BeginScope();
            var message = await _client.Api20100401Message.CreateMessage(
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
                scheduleType: null,
                sendAt: null,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: _settings.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct);

            return new SentTextMessage(RequireSid(message.Sid), message.Status?.Value);
        }, cancellationToken);
    }

    public async Task<SentTextMessage> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            using var scope = SendOnceGuardHandler.BeginScope();
            var message = await _client.Api20100401Message.CreateMessage(
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
                scheduleType: MessageEnumScheduleType.Fixed,
                sendAt: sendAt,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: null,
                fallbackFrom: null,
                messagingServiceSid: _settings.MessagingServiceSid,
                body: body,
                mediaUrl: null,
                contentSid: null,
                requestOptions: null,
                ct: ct);

            return new SentTextMessage(RequireSid(message.Sid), message.Status?.Value);
        }, cancellationToken);
    }

    public async Task CancelScheduledAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        await Bounded(async ct =>
        {
            await _client.Api20100401Message.UpdateMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                body: null,
                status: MessageEnumUpdateStatus.Canceled,
                requestOptions: null,
                ct: ct);
            return true;
        }, cancellationToken);
    }

    public async Task<TextMessageDeliveryOutcome> GetDeliveryOutcomeAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var message = await _client.Api20100401Message.FetchMessage(
                accountSid: _settings.AccountSid,
                sid: providerMessageId,
                requestOptions: null,
                ct: ct);

            return new TextMessageDeliveryOutcome(providerMessageId, message.Status?.Value, message.ErrorCode, message.ErrorMessage);
        }, cancellationToken);
    }

    public async Task RedactBodyAsync(string providerMessageId, CancellationToken cancellationToken = default)
    {
        await Bounded(async ct =>
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
    }

    public async Task<ProviderMessageListResult> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        return await Bounded(async ct =>
        {
            var messages = new List<ProviderTextMessage>();
            string? pageToken = null;

            for (var page = 0; page < MaxListPages; page++)
            {
                var response = await _client.Api20100401Message.ListMessage(
                    accountSid: _settings.AccountSid,
                    to: null,
                    from: _settings.FromNumber,
                    dateSent: null,
                    dateSentQuery: to,
                    dateSentQueryQuery: from,
                    pageSize: ListPageSize,
                    page: null,
                    pageToken: pageToken,
                    requestOptions: null,
                    ct: ct);

                if (response.Messages != null)
                {
                    messages.AddRange(response.Messages.Select(Map));
                }

                var nextPageToken = ExtractPageToken(response.NextPageUri);
                if (nextPageToken is null || nextPageToken == pageToken)
                {
                    // No next page, or the provider failed to advance the cursor: stop either way.
                    return new ProviderMessageListResult(messages, truncated: false);
                }

                pageToken = nextPageToken;
            }

            _logger.LogWarning("Reconciliation hit the page cap of {MaxPages}; the report is truncated.", MaxListPages);
            return new ProviderMessageListResult(messages, truncated: true);
        }, cancellationToken);
    }

    private static ProviderTextMessage Map(TwilioSdk.Models.ApiV2010AccountMessage message) => new()
    {
        ProviderMessageId = message.Sid,
        To = message.To,
        From = message.From,
        Status = message.Status?.Value,
        DateSent = message.DateSent,
        Body = message.Body,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage
    };

    private static string RequireSid(string? sid) =>
        sid ?? throw new MessagingProviderException("The provider accepted the message but returned no message identifier.", null);

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrEmpty(nextPageUri))
        {
            return null;
        }

        var uri = Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(new Uri("https://api.twilio.com"), nextPageUri);

        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["PageToken"];
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new MessagingProviderException("The messaging provider rejected the request.", ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new MessagingProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (DuplicateSendPreventedException ex)
        {
            throw new MessagingProviderException("The message may already have been sent; its outcome is unknown and must be settled by reconciliation.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new MessagingProviderException("The messaging provider could not be reached.", null, ex);
        }
    }
}
