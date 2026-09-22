using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using TwilioSdk;
using TwilioSdk.Core.ErrorResponse;
using TwilioSdk.Core.Exceptions;
using TwilioSdk.Models;
using TwilioSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The one place the app talks to the Twilio messaging API. Every call is bounded by a total
/// per-call deadline (a linked <see cref="CancellationTokenSource"/>), and every provider/transport
/// failure is translated to <see cref="ProviderGatewayException"/>. Phone numbers and message bodies
/// are never written to the log.
/// </summary>
public class TwilioMessagingGateway : ITwilioMessagingGateway
{
    private readonly TwilioSdkClient _client;
    private readonly TwilioSettings _settings;
    private readonly IAppLogger<TwilioMessagingGateway> _logger;

    public TwilioMessagingGateway(TwilioSdkClient client, IOptions<TwilioSettings> settings,
        IAppLogger<TwilioMessagingGateway> logger)
    {
        _client = client;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ProviderMessage> SendAsync(string toE164, string body, CancellationToken ct)
    {
        var msg = await ExecuteAsync(inner => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: null, sendAt: null, sendAsMms: null, contentVariables: null,
            riskCheck: null, from: _settings.FromNumber, fallbackFrom: null, messagingServiceSid: null,
            body: body, mediaUrl: null, contentSid: null, ct: inner), ct);
        return ToProviderMessage(msg);
    }

    public async Task<ProviderMessage> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)
    {
        // Scheduled messages must go through a Messaging Service with scheduleType=fixed + sendAt.
        var msg = await ExecuteAsync(inner => _client.Api20100401Message.CreateMessage(
            accountSid: _settings.AccountSid,
            to: toE164,
            statusCallback: null, applicationSid: null, maxPrice: null, provideFeedback: null,
            attempt: null, validityPeriod: null, forceDelivery: null, contentRetention: null,
            addressRetention: null, smartEncoded: null, persistentAction: null, trafficType: null,
            shortenUrls: null, scheduleType: MessageEnumScheduleType.Fixed, sendAt: sendAt, sendAsMms: null,
            contentVariables: null, riskCheck: null, from: null, fallbackFrom: null,
            messagingServiceSid: _settings.MessagingServiceSid, body: body, mediaUrl: null, contentSid: null,
            ct: inner), ct);
        return ToProviderMessage(msg);
    }

    public async Task<ProviderMessage> FetchAsync(string sid, CancellationToken ct)
    {
        var msg = await ExecuteAsync(inner => _client.Api20100401Message.FetchMessage(
            accountSid: _settings.AccountSid, sid: sid, ct: inner), ct);
        return ToProviderMessage(msg);
    }

    public async Task CancelScheduledAsync(string sid, CancellationToken ct)
    {
        // status=canceled cancels a not-yet-sent scheduled message.
        await ExecuteAsync(inner => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: sid, body: null,
            status: MessageEnumUpdateStatus.Canceled, ct: inner), ct);
    }

    public async Task RedactContentAsync(string sid, CancellationToken ct)
    {
        // An empty body redacts the message text at the provider while keeping the record.
        await ExecuteAsync(inner => _client.Api20100401Message.UpdateMessage(
            accountSid: _settings.AccountSid, sid: sid, body: string.Empty, status: null, ct: inner), ct);
    }

    public async Task<ProviderMessageListing> ListForFromNumberAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<ProviderMessageSummary>();
        int page = 0;
        int pagesRead = 0;
        bool truncated = false;

        while (true)
        {
            // Ask the provider for THIS number's messages in range; DateSent> = from, DateSent< = to.
            var resp = await ExecuteAsync(inner => _client.Api20100401Message.ListMessage(
                accountSid: _settings.AccountSid,
                to: null,
                from: _settings.FromNumber,
                dateSent: null,
                dateSentQuery: to,
                dateSentQueryQuery: from,
                pageSize: _settings.ListPageSize,
                page: page,
                pageToken: null,
                ct: inner), ct);

            pagesRead++;

            if (resp.Messages is { Count: > 0 })
                results.AddRange(resp.Messages.Select(ToSummary));

            if (string.IsNullOrEmpty(resp.NextPageUri))
                break;

            if (pagesRead >= _settings.MaxReconciliationPages)
            {
                truncated = true;
                break;
            }

            page++;
        }

        return new ProviderMessageListing(results, truncated, pagesRead);
    }

    private static ProviderMessage ToProviderMessage(ApiV2010AccountMessage msg) =>
        new(
            Sid: msg.Sid ?? string.Empty,
            Status: msg.Status?.Value ?? "unknown",
            ErrorCode: msg.ErrorCode,
            ErrorMessage: msg.ErrorMessage,
            DateSent: ParseProviderDate(msg.DateSent));

    private static ProviderMessageSummary ToSummary(ApiV2010AccountMessage msg) =>
        new(
            Sid: msg.Sid ?? string.Empty,
            Status: msg.Status?.Value ?? "unknown",
            To: msg.To,
            DateSent: ParseProviderDate(msg.DateSent));

    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private Task ExecuteAsync(Func<CancellationToken, Task> call, CancellationToken ct) =>
        ExecuteAsync<object?>(async inner => { await call(inner); return null; }, ct);

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        // A total budget for the whole call (per-attempt Timeout alone does not bound retries).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_settings.CallTimeoutSeconds));

        try
        {
            return await call(cts.Token);
        }
        catch (SdkException<RawError> ex)
        {
            var status = ex.Error.StatusCode;
            // Log status + Twilio error code / more_info only — never the body's message (it can echo the number).
            LogProviderError(status, ex.Error);
            throw new ProviderGatewayException($"Provider returned HTTP {(int)status}.", ex, status);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Twilio returned a response that could not be processed: {Error}", ex.Message);
            throw new ProviderGatewayException("The provider returned a response that could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            // Caller-initiated cancellation propagates; anything else is a transport failure / timeout.
            if (ct.IsCancellationRequested) throw;
            _logger.LogWarning("Twilio messaging API was unreachable or timed out: {Error}", ex.Message);
            throw new ProviderGatewayException("Provider unreachable or timed out.", ex);
        }
    }

    private void LogProviderError(System.Net.HttpStatusCode status, RawError error)
    {
        try
        {
            var body = error.ReadAsJson<TwilioErrorBody>();
            _logger.LogWarning("Twilio messaging API error: HTTP {Status}, code {Code}, more_info {MoreInfo}",
                (int)status, body?.Code, body?.MoreInfo);
        }
        catch
        {
            _logger.LogWarning("Twilio messaging API error: HTTP {Status}", (int)status);
        }
    }

    /// <summary>Non-PII subset of Twilio's error body (message omitted deliberately — it can contain the number).</summary>
    private sealed record TwilioErrorBody
    {
        [JsonPropertyName("code")] public int? Code { get; init; }
        [JsonPropertyName("more_info")] public string? MoreInfo { get; init; }
    }
}
