using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Core.Authentication.Basic;
using Twilio.Core.Configuration;
using Twilio.Core.ErrorResponse;
using Twilio.Core.Exceptions;
using Twilio.Models;
using Twilio.Models.Enums;
using Twilio.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioSmsGateway : ISmsGateway
{
    private readonly TwilioClient _client;
    private readonly TwilioOptions _options;
    private readonly ILogger<TwilioSmsGateway> _logger;

    public TwilioSmsGateway(TwilioClient client, IOptions<TwilioOptions> options, ILogger<TwilioSmsGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PhoneLookupResult> LookupAsync(string phoneNumber, CancellationToken ct)
    {
        try
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
                ct: ct);

            var usable = response.Valid == true && !string.IsNullOrWhiteSpace(response.PhoneNumber);
            return new PhoneLookupResult(true, usable, response.PhoneNumber, HttpStatus: null);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Phone lookup rejected by provider with HTTP {StatusCode}.", status);
            return new PhoneLookupResult(false, false, null, status);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Phone lookup returned a response that could not be processed.");
            throw new InvalidOperationException("The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Phone lookup could not reach the provider.");
            return new PhoneLookupResult(false, false, null, HttpStatus: null);
        }
    }

    public async Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken ct)
    {
        try
        {
            var scheduled = sendAt.HasValue;
            var created = await _client.Api20100401Message.CreateMessage(
                accountSid: _options.AccountSid,
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
                scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
                sendAt: sendAt,
                sendAsMms: null,
                contentVariables: null,
                riskCheck: null,
                from: scheduled ? null : _options.FromNumber,
                fallbackFrom: null,
                messagingServiceSid: scheduled ? _options.MessagingServiceSid : null,
                body: body,
                mediaUrl: null,
                contentSid: null,
                ct: ct);

            return Map(created, accepted: true);
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("CreateMessage failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new ProviderMessage(false, null, "send_failed", null, (int)ex.Error.StatusCode, null, to, null, DateTimeOffset.UtcNow);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "CreateMessage returned a response that could not be processed.");
            return new ProviderMessage(false, null, "send_failed", null, null, null, to, null, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "CreateMessage could not reach the provider.");
            return new ProviderMessage(false, null, "send_failed", null, null, null, to, null, DateTimeOffset.UtcNow);
        }
    }

    public async Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct)
    {
        var message = await ExecuteMessageRead(() => _client.Api20100401Message.FetchMessage(
            accountSid: _options.AccountSid,
            sid: sid,
            ct: ct));
        return Map(message, accepted: true);
    }

    public async Task<ProviderMessagePage> ListSentFromAsync(DateTimeOffset rangeFrom, DateTimeOffset rangeTo, string? pageToken, CancellationToken ct)
    {
        try
        {
            var page = await _client.Api20100401Message.ListMessage(
                accountSid: _options.AccountSid,
                to: null,
                from: _options.FromNumber,
                dateSent: null,
                dateSentQuery: rangeTo,
                dateSentQueryQuery: rangeFrom,
                pageSize: 1000,
                page: null,
                pageToken: pageToken,
                ct: ct);

            var messages = page.Messages?.Select(m => Map(m, accepted: true)).ToList()
                           ?? new List<ProviderMessage>();
            return new ProviderMessagePage(messages, ExtractPageToken(page.NextPageUri));
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("ListMessage failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("The provider could not list messages for reconciliation.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("The messaging provider is unreachable.", ex);
        }
    }

    public async Task<ProviderMessage> RedactBodyAsync(string sid, CancellationToken ct)
    {
        var updated = await ExecuteMessageWrite(() => _client.Api20100401Message.UpdateMessage(
            accountSid: _options.AccountSid,
            sid: sid,
            body: string.Empty,
            status: null,
            ct: ct));
        return Map(updated, accepted: true);
    }

    public async Task<ProviderMessage> CancelAsync(string sid, CancellationToken ct)
    {
        var updated = await ExecuteMessageWrite(() => _client.Api20100401Message.UpdateMessage(
            accountSid: _options.AccountSid,
            sid: sid,
            body: null,
            status: MessageEnumUpdateStatus.Canceled,
            ct: ct));
        return Map(updated, accepted: true);
    }

    private async Task<ApiV2010AccountMessage> ExecuteMessageRead(Func<Task<ApiV2010AccountMessage>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Message read failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("The provider could not return the message.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("The messaging provider is unreachable.", ex);
        }
    }

    private async Task<ApiV2010AccountMessage> ExecuteMessageWrite(Func<Task<ApiV2010AccountMessage>> call)
    {
        try
        {
            return await call();
        }
        catch (SdkException<RawError> ex)
        {
            _logger.LogWarning("Message update failed with HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            throw new InvalidOperationException("The provider could not update the message.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new InvalidOperationException("The messaging provider is unreachable.", ex);
        }
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message, bool accepted)
    {
        return new ProviderMessage(
            accepted,
            message.Sid,
            message.Status?.Value ?? "unknown",
            message.Body,
            message.ErrorCode,
            message.ErrorMessage,
            message.To,
            message.From,
            ParseTimestamp(message.DateCreated));
    }

    private static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ExtractPageToken(string? nextPageUri)
    {
        if (string.IsNullOrWhiteSpace(nextPageUri))
        {
            return null;
        }

        var absolute = nextPageUri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? nextPageUri
            : "https://api.twilio.com" + nextPageUri;
        if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("PageToken", StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    public static TwilioClientOptions CreateClientOptions(TwilioOptions settings, ILoggerFactory loggerFactory)
    {
        var options = new TwilioClientOptions
        {
            Environment = ServerEnvironment.Production,
            AccountSidAuthToken = new BasicAuthCredentials
            {
                Username = settings.AccountSid,
                Password = settings.AuthToken
            },
            Retry = RetryOptions.Default() with { Timeout = TimeSpan.FromSeconds(10) },
            Logging = new LoggingOptions
            {
                LoggerFactory = loggerFactory,
                LogRequestBody = false,
                LogRequestHeaders = false,
                LogResponseHeaders = false,
                RedactedKeys =
                [
                    "sig", "signature", "access_token", "apikey", "api_key",
                    "client_secret", "password", "refresh_token", "code", "assertion", "client_assertion",
                    "To", "From", "Body", "MessagingServiceSid"
                ]
            }
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            options.Server.Default.Production.BaseUrl = settings.BaseUrl;
        }

        return options;
    }
}
