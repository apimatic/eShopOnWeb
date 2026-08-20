using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public sealed class TwilioOrderMessagingGateway : IOrderMessagingGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ListBudget = TimeSpan.FromSeconds(45);
    private const int MaxListPages = 20;
    private const long ListPageSize = 100;

    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly ILogger<TwilioOrderMessagingGateway> _logger;

    public TwilioOrderMessagingGateway(
        TwilioSdkClient client,
        IOptions<TwilioSettings> settings,
        ILogger<TwilioOrderMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public Task<PhoneNumberLookup> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        return Bounded(ct => LookupCoreAsync(phoneNumber, ct), cancellationToken, CallBudget);
    }

    public Task<ProviderMessage> SendAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken cancellationToken)
    {
        return WriteOnce(ct => CreateCoreAsync(to, body, sendAt, ct), cancellationToken, CallBudget);
    }

    public Task<ProviderMessage> FetchAsync(string sid, CancellationToken cancellationToken)
    {
        return Bounded(ct => FetchCoreAsync(sid, ct), cancellationToken, CallBudget);
    }

    public Task<ProviderMessage> CancelScheduledAsync(string sid, CancellationToken cancellationToken)
    {
        return WriteOnce(
            ct => UpdateCoreAsync(sid, body: null, status: MessageEnumUpdateStatus.Canceled, ct),
            cancellationToken,
            CallBudget);
    }

    public Task<ProviderMessage> RedactBodyAsync(string sid, CancellationToken cancellationToken)
    {
        return WriteOnce(
            ct => UpdateCoreAsync(sid, body: string.Empty, status: null, ct),
            cancellationToken,
            CallBudget);
    }

    public Task<(IReadOnlyList<ProviderMessage> Messages, bool Truncated)> ListSentFromConfiguredNumberAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        return Bounded(ct => ListCoreAsync(from, to, ct), cancellationToken, ListBudget);
    }

    private async Task<PhoneNumberLookup> LookupCoreAsync(string phoneNumber, CancellationToken ct)
    {
        var lookup = await _client.LookupsV2PhoneNumber.FetchPhoneNumber3(
            phoneNumber: phoneNumber,
            fields: "line_type_intelligence",
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

        var errors = lookup.ValidationErrors?
            .Select(e => e.Value)
            .Where(e => !string.IsNullOrEmpty(e))
            .Select(e => e!)
            .ToList() ?? new List<string>();

        return new PhoneNumberLookup(
            lookup.PhoneNumber ?? string.Empty,
            lookup.Valid == true,
            errors,
            lookup.LineTypeIntelligence?.Type);
    }

    private async Task<ProviderMessage> CreateCoreAsync(string to, string body, DateTimeOffset? sendAt, CancellationToken ct)
    {
        var scheduled = sendAt.HasValue;
        var created = await _client.Api20100401Message.CreateMessage(
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
            scheduleType: scheduled ? MessageEnumScheduleType.Fixed : null,
            sendAt: sendAt,
            sendAsMms: null,
            contentVariables: null,
            riskCheck: null,
            from: _settings.FromNumber,
            fallbackFrom: null,
            messagingServiceSid: scheduled ? _settings.MessagingServiceSid : null,
            body: body,
            mediaUrl: null,
            contentSid: null,
            ct: ct);

        _logger.LogInformation("Provider accepted message {Sid} with status {Status}.", created.Sid, created.Status);
        return Map(created);
    }

    private async Task<ProviderMessage> FetchCoreAsync(string sid, CancellationToken ct)
    {
        var message = await _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid,
            sid: sid,
            ct: ct);
        return Map(message);
    }

    private async Task<ProviderMessage> UpdateCoreAsync(
        string sid,
        string? body,
        MessageEnumUpdateStatus? status,
        CancellationToken ct)
    {
        var updated = await _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid,
            sid: sid,
            body: body,
            status: status,
            ct: ct);
        _logger.LogInformation("Provider updated message {Sid} with status {Status}.", updated.Sid, updated.Status);
        return Map(updated);
    }

    private async Task<(IReadOnlyList<ProviderMessage> Messages, bool Truncated)> ListCoreAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var messages = new List<ProviderMessage>();
        string? pageToken = null;
        int? page = null;
        var truncated = false;

        for (var pages = 0; pages < MaxListPages; pages++)
        {
            var response = await _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: ListPageSize,
                page: page,
                pageToken: pageToken,
                ct: ct);

            if (response.Messages is not null)
            {
                messages.AddRange(response.Messages.Select(Map));
            }

            if (string.IsNullOrEmpty(response.NextPageUri))
            {
                return (messages, truncated);
            }

            pageToken = TryGetQueryValue(response.NextPageUri, "PageToken");
            var pageText = TryGetQueryValue(response.NextPageUri, "Page");
            page = int.TryParse(pageText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPage)
                ? parsedPage
                : null;

            if (string.IsNullOrEmpty(pageToken) && page is null)
            {
                truncated = true;
                _logger.LogWarning("Reconciliation paging stopped because NextPageUri did not contain PageToken or Page.");
                return (messages, truncated);
            }
        }

        _logger.LogWarning("Reconciliation list stopped after {MaxPages} pages.", MaxListPages);
        return (messages, Truncated: true);
    }

    private async Task<T> WriteOnce<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, TimeSpan budget)
    {
        using var scope = new TwilioOnceWriteScope();
        return await Bounded(call, ct, budget);
    }

    private async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct, TimeSpan budget)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = (int)ex.Error.StatusCode;
            _logger.LogWarning("Messaging provider returned HTTP {StatusCode}.", status);
            throw new OrderMessagingException("The messaging provider rejected the request.", status, ex);
        }
        catch (JsonException ex)
        {
            throw new OrderMessagingException("The provider returned a response that could not be processed.", inner: ex);
        }
        catch (TwilioDuplicateWriteException ex)
        {
            throw new OrderMessagingException("The provider write outcome is unknown.", inner: ex);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OrderMessagingException("The messaging provider timed out.");
        }
        catch (HttpRequestException ex)
        {
            throw new OrderMessagingException("The messaging provider is unreachable.", inner: ex);
        }
    }

    private static ProviderMessage Map(ApiV2010AccountMessage message)
    {
        return new ProviderMessage(
            message.Sid ?? string.Empty,
            message.Status?.Value,
            message.To,
            message.From,
            message.Body,
            message.DateCreated,
            message.DateSent,
            message.DateUpdated,
            message.ErrorCode,
            message.ErrorMessage,
            message.MessagingServiceSid,
            message.Direction?.Value);
    }

    private static string? TryGetQueryValue(string uri, string name)
    {
        var absolute = uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new Uri(uri)
            : new Uri("https://api.twilio.com" + (uri.StartsWith('/') ? uri : "/" + uri));
        var query = absolute.Query.TrimStart('?');
        if (query.Length == 0)
        {
            return null;
        }

        foreach (var part in query.Split('&'))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[0]);
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Length > 1 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
        }

        return null;
    }
}
