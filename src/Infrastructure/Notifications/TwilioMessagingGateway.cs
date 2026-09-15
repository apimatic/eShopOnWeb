using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;
using TwilioSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

public interface ITwilioMessagingGateway
{
    Task<string?> ValidateAndCanonicalizeAsync(string number, string? countryCode, CancellationToken ct);
    Task<ProviderMessage> SendAsync(string destination, string content, CancellationToken ct);
    Task<ProviderMessage> ScheduleAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken ct);
    Task<ProviderMessage> CancelAsync(string providerMessageId, CancellationToken ct);
    Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken ct);
    Task<ProviderMessage> RedactAsync(string providerMessageId, CancellationToken ct);
    Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed record ProviderMessage(string ProviderMessageId, string Status, int? ErrorCode,
    DateTimeOffset? DateCreated, DateTimeOffset? DateSent, DateTimeOffset? DateUpdated, string? Body);

public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, int? statusCode, Exception innerException)
        : base(message, innerException) => StatusCode = statusCode;

    public TwilioProviderException(string message) : base(message) { }
    public int? StatusCode { get; }
}

public sealed class TwilioMessagingGateway : ITwilioMessagingGateway
{
    public const string MessagingHttpClientName = "TwilioMessaging";
    public const string LookupHttpClientName = "TwilioLookup";
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private readonly TwilioSdkClient _messagingClient;
    private readonly TwilioSdkClient _lookupClient;
    private readonly TwilioSettings _settings;

    public TwilioMessagingGateway(IHttpClientFactory factory, IOptions<TwilioSettings> settings)
    {
        _settings = settings.Value;
        _messagingClient = CreateClient(factory.CreateClient(MessagingHttpClientName), applyBaseUrl: true);
        _lookupClient = CreateClient(factory.CreateClient(LookupHttpClientName), applyBaseUrl: false);
    }

    public async Task<string?> ValidateAndCanonicalizeAsync(string number, string? countryCode, CancellationToken ct)
    {
        var response = await ExecuteAsync(token => _lookupClient.LookupsV2PhoneNumber.FetchPhoneNumber3(
            phoneNumber: number,
            fields: null,
            countryCode: countryCode,
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
            ct: token), ct);

        return response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber)
            ? response.PhoneNumber
            : null;
    }

    public Task<ProviderMessage> SendAsync(string destination, string content, CancellationToken ct) =>
        CreateMessageAsync(destination, content, null, false, ct);

    public Task<ProviderMessage> ScheduleAsync(string destination, string content, DateTimeOffset sendAt, CancellationToken ct) =>
        CreateMessageAsync(destination, content, sendAt.ToUniversalTime(), true, ct);

    public async Task<ProviderMessage> CancelAsync(string providerMessageId, CancellationToken ct)
    {
        var response = await ExecuteAsync(token => _messagingClient.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: token), ct);
        return Map(response);
    }

    public async Task<ProviderMessage> FetchAsync(string providerMessageId, CancellationToken ct)
    {
        var response = await ExecuteAsync(token => _messagingClient.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            ct: token), ct);
        return Map(response);
    }

    public async Task<ProviderMessage> RedactAsync(string providerMessageId, CancellationToken ct)
    {
        var response = await ExecuteAsync(token => _messagingClient.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: providerMessageId,
            body: string.Empty,
            status: null,
            ct: token), ct);
        return Map(response);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        // The generated SDK maps these arguments to the literal DateSent> and DateSent<
        // provider filters. Pad both UTC date boundaries so the provider query remains a
        // superset even when those operators are treated as strict; the caller's exact
        // inclusive interval is enforced below.
        var lower = new DateTimeOffset(from.UtcDateTime.Date, TimeSpan.Zero).AddDays(-1);
        var upper = new DateTimeOffset(to.UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var messages = new List<ProviderMessage>();
        var observedTokens = new HashSet<string>(StringComparer.Ordinal);
        string? pageToken = null;

        for (var pageCount = 0; pageCount < 10_000; pageCount++)
        {
            var page = await ExecuteAsync(token => _messagingClient.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: upper,
                dateSentQueryQuery: lower,
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                ct: token), ct);

            foreach (var item in page.Messages ?? Array.Empty<ApiV2010AccountMessage>())
            {
                var mapped = Map(item);
                // Scheduled messages can be canceled before sending and therefore have no
                // DateSent. Their provider record still belongs to the range in which it was
                // created and must participate in reconciliation.
                var occurredAt = mapped.DateSent ?? mapped.DateCreated;
                if (occurredAt is { } timestamp && timestamp >= from && timestamp <= to)
                {
                    messages.Add(mapped);
                }
            }

            if (string.IsNullOrWhiteSpace(page.NextPageUri))
            {
                return messages;
            }

            pageToken = ReadPageToken(page.NextPageUri);
            if (string.IsNullOrWhiteSpace(pageToken) || !observedTokens.Add(pageToken))
            {
                throw new TwilioProviderException("Twilio pagination did not make progress.");
            }
        }

        throw new TwilioProviderException("Twilio pagination exceeded its safety limit; no partial report was returned.");
    }

    private async Task<ProviderMessage> CreateMessageAsync(string destination, string content,
        DateTimeOffset? sendAt, bool scheduled, CancellationToken ct)
    {
        var response = await ExecuteAsync(token => _messagingClient.Api20100401Message.CreateMessage(
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
            scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
            body: content,
            mediaUrl: null,
            contentSid: null,
            ct: token), ct);
        return Map(response);
    }

    private TwilioSdkClient CreateClient(HttpClient httpClient, bool applyBaseUrl)
    {
        var options = new TwilioSdkClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = _settings.AccountSid,
                Password = _settings.AuthToken
            },
            Retry = RetryOptions.Default() with
            {
                MaxRetries = 1,
                Timeout = TimeSpan.FromSeconds(8)
            }
        };

        if (applyBaseUrl && !string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = _settings.BaseUrl;
        }

        return new TwilioSdkClient(httpClient, options);
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Sid))
        {
            throw new TwilioProviderException("Twilio returned a message without an identifier.");
        }

        return new ProviderMessage(
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.ErrorCode,
            ParseProviderDate(message.DateCreated),
            ParseProviderDate(message.DateSent),
            ParseProviderDate(message.DateUpdated),
            message.Body);
    }

    private static DateTimeOffset? ParseProviderDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var result)
            ? result
            : null;

    private static string? ReadPageToken(string nextPageUri)
    {
        var query = nextPageUri;
        if (Uri.TryCreate(nextPageUri, UriKind.Absolute, out var absolute))
        {
            query = absolute.Query;
        }
        else if (nextPageUri.IndexOf('?') is var marker && marker >= 0)
        {
            query = nextPageUri[(marker + 1)..];
        }

        foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2 && string.Equals(Uri.UnescapeDataString(pair[0]), "PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
        }

        return null;
    }

    private static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(CallBudget);
        try
        {
            return await operation(deadline.Token);
        }
        catch (SdkException<RawError> ex)
        {
            throw new TwilioProviderException("Twilio rejected the request.", (int)ex.Error.StatusCode, ex);
        }
        catch (JsonException ex)
        {
            throw new TwilioProviderException("Twilio returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new TwilioProviderException("Twilio could not be reached within the configured deadline.", null, ex);
        }
    }
}
