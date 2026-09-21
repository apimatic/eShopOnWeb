using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// In-memory stand-in for <see cref="ISmsGateway"/> so the endpoint tests exercise the full flow without
/// touching the live provider. It records what it was asked to send so tests can assert behaviour and so
/// reconciliation has data to line up against.
/// </summary>
public sealed class FakeSmsGateway : ISmsGateway
{
    public sealed record SentMessage(string Sid, string To, string Body, bool Scheduled, DateTimeOffset At)
    {
        public string Status { get; set; } = Scheduled ? "scheduled" : "queued";
        public bool Redacted { get; set; }
        public int? ErrorCode { get; set; }
    }

    private int _counter;
    public ConcurrentDictionary<string, SentMessage> Messages { get; } = new();

    /// <summary>When set, a number containing this substring is treated as an unusable destination.</summary>
    public string UnusableMarker { get; set; } = "invalid";

    /// <summary>Status returned by <see cref="FetchStatusAsync"/> (simulates the provider's evolving outcome).</summary>
    public string FetchStatusValue { get; set; } = "delivered";

    /// <summary>When true, every send is reported as not accepted (simulates a provider rejection).</summary>
    public bool FailSends { get; set; }

    public string SendingNumber => "+15005550006";

    public Task<SmsValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Contains(UnusableMarker, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new SmsValidationResult(false, null, "The number is not a usable destination."));
        }

        // Canonicalise to a stable E.164-ish form (strip non-digits, ensure leading +).
        var digits = new string(phoneNumber.Where(char.IsDigit).ToArray());
        var canonical = "+" + digits;
        return Task.FromResult(new SmsValidationResult(true, canonical, null));
    }

    public Task<SmsDispatchResult> SendAsync(string toE164, string body, CancellationToken ct) =>
        Task.FromResult(Record(toE164, body, scheduled: false));

    public Task<SmsDispatchResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct) =>
        Task.FromResult(Record(toE164, body, scheduled: true, sendAt));

    private SmsDispatchResult Record(string to, string body, bool scheduled, DateTimeOffset? at = null)
    {
        if (FailSends)
        {
            return new SmsDispatchResult(false, null, null, null, null, "Simulated provider rejection.");
        }

        var sid = (scheduled ? "SM_sched_" : "SM_") + Interlocked.Increment(ref _counter);
        var msg = new SentMessage(sid, to, body, scheduled, at ?? DateTimeOffset.UtcNow);
        Messages[sid] = msg;
        return new SmsDispatchResult(true, sid, msg.Status, null, null, null);
    }

    public Task<SmsStatusResult> FetchStatusAsync(string messageSid, CancellationToken ct)
    {
        if (Messages.TryGetValue(messageSid, out var msg))
        {
            return Task.FromResult(new SmsStatusResult(true, msg.Status == "scheduled" ? "scheduled" : FetchStatusValue, msg.ErrorCode, null));
        }
        return Task.FromResult(new SmsStatusResult(false, null, null, null));
    }

    public Task<bool> CancelScheduledAsync(string messageSid, CancellationToken ct)
    {
        if (Messages.TryGetValue(messageSid, out var msg))
        {
            msg.Status = "canceled";
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task RedactContentAsync(string messageSid, CancellationToken ct)
    {
        if (Messages.TryGetValue(messageSid, out var msg))
        {
            msg.Redacted = true;
        }
        return Task.CompletedTask;
    }

    public Task<SmsListResult> ListSentAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var inRange = Messages.Values
            .Where(m => m.At >= from && m.At <= to)
            .Select(m => new SmsProviderMessage(m.Sid, m.Status, SendingNumber, m.At, m.ErrorCode))
            .ToList();
        return Task.FromResult(new SmsListResult(inRange, false));
    }
}
