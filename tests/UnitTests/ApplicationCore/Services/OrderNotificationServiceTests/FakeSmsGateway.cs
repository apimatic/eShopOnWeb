using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.OrderNotificationServiceTests;

/// <summary>A controllable in-memory stand-in for the SMS provider, so the service is tested without Twilio.</summary>
public class FakeSmsGateway : ISmsGateway
{
    public bool ValidationSucceeds { get; set; } = true;
    public string CanonicalNumber { get; set; } = "+15145550100";
    public bool ThrowOnSend { get; set; }
    public bool ThrowOnCancel { get; set; }

    public List<(string To, string Body)> Sent { get; } = new();
    public List<(string To, string Body, DateTimeOffset At)> Scheduled { get; } = new();
    public List<string> Canceled { get; } = new();
    public List<string> Redacted { get; } = new();
    public List<ProviderMessage> ProviderMessages { get; } = new();
    public Dictionary<string, SmsStatusResult> StatusBySid { get; } = new();

    private int _sidCounter;

    public Task<PhoneValidationResult> ValidatePhoneNumberAsync(string rawPhoneNumber, CancellationToken ct = default)
        => Task.FromResult(new PhoneValidationResult(ValidationSucceeds, ValidationSucceeds ? CanonicalNumber : null, "national"));

    public Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        if (ThrowOnSend)
        {
            throw new SmsGatewayException("send failed", statusCode: 400);
        }
        Sent.Add((toNumber, body));
        return Task.FromResult(new SmsDispatchResult($"SM{++_sidCounter}", "queued"));
    }

    public Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAtUtc, CancellationToken ct = default)
    {
        Scheduled.Add((toNumber, body, sendAtUtc));
        return Task.FromResult(new SmsDispatchResult($"SM{++_sidCounter}", "scheduled"));
    }

    public Task CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        if (ThrowOnCancel)
        {
            throw new SmsGatewayException("cancel failed", statusCode: 500);
        }
        Canceled.Add(messageSid);
        return Task.CompletedTask;
    }

    public Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct = default)
        => Task.FromResult(StatusBySid.TryGetValue(messageSid, out var s) ? s : new SmsStatusResult("queued", null));

    public Task RedactContentAsync(string messageSid, CancellationToken ct = default)
    {
        Redacted.Add(messageSid);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessage>> ListOwnMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<ProviderMessage>)ProviderMessages);
}
