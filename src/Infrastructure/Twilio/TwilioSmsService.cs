using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Twilio-backed ISmsService. All SDK types stay inside this class; callers see only
/// ApplicationCore DTOs. Never logs phone numbers or credentials — message Sids and
/// provider status codes only.
/// </summary>
public class TwilioSmsService : ISmsService
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int MaxListPages = 50;
    private const long ListPageSize = 1000;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioSmsService> _logger;

    public TwilioSmsService(TwilioSdkClient client, IOptions<TwilioSettings> settings, IAppLogger<TwilioSmsService> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<PhoneNumberValidation> ValidatePhoneNumberAsync(string phoneNumber, CancellationToken ct = default)
    {
        try
        {
            var response = await Bounded(c => _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
                phoneNumber,
                fields: null, countryCode: null, firstName: null, lastName: null,
                addressLine1: null, addressLine2: null, city: null, state: null,
                postalCode: null, addressCountryCode: null, nationalId: null,
                dateOfBirth: null, lastVerifiedDate: null, verificationSid: null,
                partnerSubId: null, requestOptions: null, ct: c), ct);

            if (response.Valid == true)
            {
                return new PhoneNumberValidation(true, response.PhoneNumber, null);
            }

            var reason = response.ValidationErrors is { Count: > 0 }
                ? string.Join(", ", response.ValidationErrors.Select(e => e.Value))
                : "The provider does not consider this a usable destination.";
            return new PhoneNumberValidation(false, null, reason);
        }
        catch (SdkException<RawError> ex)
        {
            // The provider may answer an unusable number with a non-2xx instead of Valid=false.
            _logger.LogWarning("Phone number validation rejected by provider: HTTP {StatusCode}.", (int)ex.Error.StatusCode);
            return new PhoneNumberValidation(false, null, "The provider does not consider this a usable destination.");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", null, ex);
        }
    }

    public async Task<SentMessage> SendMessageAsync(string to, string body, CancellationToken ct = default)
    {
        var message = await Guarded(c => _client.Api20100401Message.CreateMessage(
            _settings.AccountSid, to,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: _settings.FromNumber, fallbackFrom: null,
            messagingServiceSid: null, body: body, mediaUrl: null, contentSid: null,
            requestOptions: null, ct: c), ct);

        return new SentMessage(message.Sid!, message.Status?.Value ?? MessageStatuses.Queued);
    }

    public async Task<SentMessage> ScheduleMessageAsync(string to, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        var message = await Guarded(c => _client.Api20100401Message.CreateMessage(
            _settings.AccountSid, to,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null, contentSid: null,
            requestOptions: null, ct: c), ct);

        return new SentMessage(message.Sid!, message.Status?.Value ?? MessageStatuses.Scheduled);
    }

    public async Task CancelScheduledMessageAsync(string messageSid, CancellationToken ct = default)
    {
        await Guarded(c => _client.Api20100401Message.UpdateMessage(
            _settings.AccountSid, messageSid,
            body: null, status: MessageEnumUpdateStatus.Canceled,
            requestOptions: null, ct: c), ct);
    }

    public async Task<ProviderMessage> GetMessageAsync(string messageSid, CancellationToken ct = default)
    {
        var message = await Guarded(c => _client.Api20100401Message.FetchMessage(
            _settings.AccountSid, messageSid, requestOptions: null, ct: c), ct);
        return Map(message);
    }

    public async Task RedactMessageBodyAsync(string messageSid, CancellationToken ct = default)
    {
        await Guarded(c => _client.Api20100401Message.UpdateMessage(
            _settings.AccountSid, messageSid,
            body: "", status: null,
            requestOptions: null, ct: c), ct);
    }

    public async Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<ProviderMessage>();
        int? page = null;
        string? pageToken = null;

        for (var pageCount = 0; pageCount < MaxListPages; pageCount++)
        {
            var currentPage = page;
            var currentToken = pageToken;
            var response = await Guarded(c => _client.Api20100401Message.ListMessage(
                _settings.AccountSid,
                to: null, from: _settings.FromNumber, dateSent: null,
                dateSentQuery: to, dateSentQueryQuery: from,
                pageSize: ListPageSize, page: currentPage, pageToken: currentToken,
                requestOptions: null, ct: c), ct);

            if (response.Messages != null)
            {
                results.AddRange(response.Messages.Select(Map));
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                return results;
            }

            pageToken = ExtractQueryParam(response.NextPageUri!, "PageToken");
            var pageValue = ExtractQueryParam(response.NextPageUri!, "Page");
            page = pageValue != null && int.TryParse(pageValue, out var parsed) ? parsed : (response.Page ?? 0) + 1;
            if (pageToken == null)
            {
                // No-progress guard: without a token the next request would repeat this page.
                _logger.LogWarning("Message listing stopped: next page had no page token.");
                return results;
            }
        }

        _logger.LogWarning("Message listing hit the page cap of {MaxPages}; the reconciliation range may be truncated.", MaxListPages);
        return results;
    }

    private async Task<T> Guarded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await Bounded(call, ct);
        }
        catch (SdkException<RawError> ex)
        {
            throw new SmsProviderException(
                $"The messaging provider rejected the request (HTTP {(int)ex.Error.StatusCode}).",
                ex.Error.StatusCode, ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new SmsProviderException("The provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new SmsProviderException("The messaging provider is unreachable.", null, ex);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        DateTimeOffset? dateSent = DateTimeOffset.TryParse(message.DateSent, out var parsed) ? parsed : null;
        return new ProviderMessage(message.Sid!, message.To, message.From,
            message.Status?.Value, message.ErrorCode, message.ErrorMessage, dateSent);
    }

    private static string? ExtractQueryParam(string uri, string name)
    {
        var queryIndex = uri.IndexOf('?');
        if (queryIndex < 0)
        {
            return null;
        }

        foreach (var pair in uri[(queryIndex + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], name, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }
        return null;
    }
}
