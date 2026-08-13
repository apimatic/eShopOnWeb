using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.SmsNotifications;

/// <summary>
/// A deterministic in-memory stand-in for <see cref="ISmsProvider"/>, so the endpoint flows can be exercised
/// without sending real messages. It records every send/schedule/cancel/redact so tests can assert on them.
/// </summary>
public class FakeSmsProvider : ISmsProvider
{
    private readonly object _lock = new();
    private int _counter;
    private readonly Dictionary<string, string> _status = new();

    public string SendingNumber => "+15550000000";

    public List<SentRecord> Sends { get; } = new();
    public List<string> CanceledSids { get; } = new();
    public List<string> RedactedSids { get; } = new();

    public int ImmediateSendCount => Sends.Count(s => s.Kind == "send");
    public int ScheduleCount => Sends.Count(s => s.Kind == "schedule");

    public Task<PhoneNumberValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        // A sentinel input models a number the provider rejects as unusable.
        if (rawNumber.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(new PhoneNumberValidationResult(false, null));
        }

        var digits = new string(rawNumber.Where(char.IsDigit).ToArray());
        return Task.FromResult(new PhoneNumberValidationResult(true, "+" + digits));
    }

    public Task<SentSmsMessage> SendAsync(string toNumber, string body, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var sid = $"SMsend{Interlocked.Increment(ref _counter)}";
            Sends.Add(new SentRecord(toNumber, body, sid, "send"));
            _status[sid] = "delivered";
            return Task.FromResult(new SentSmsMessage(sid, "queued"));
        }
    }

    public Task<SentSmsMessage> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var sid = $"SMsched{Interlocked.Increment(ref _counter)}";
            Sends.Add(new SentRecord(toNumber, body, sid, "schedule"));
            _status[sid] = "scheduled";
            return Task.FromResult(new SentSmsMessage(sid, "scheduled"));
        }
    }

    public Task CancelScheduledAsync(string messageSid, CancellationToken ct = default)
    {
        lock (_lock)
        {
            CanceledSids.Add(messageSid);
            _status[messageSid] = "canceled";
        }
        return Task.CompletedTask;
    }

    public Task<string?> FetchStatusAsync(string messageSid, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_status.TryGetValue(messageSid, out var s) ? s : null);
        }
    }

    public Task RedactContentAsync(string messageSid, CancellationToken ct = default)
    {
        lock (_lock)
        {
            RedactedSids.Add(messageSid);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ProviderMessageRecord> list = Sends
                .Select(s => new ProviderMessageRecord(s.Sid, s.To, SendingNumber, _status[s.Sid], DateTimeOffset.UtcNow, s.Body))
                .ToList();
            return Task.FromResult(list);
        }
    }

    public record SentRecord(string To, string Body, string Sid, string Kind);
}
