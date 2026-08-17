using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace PublicApiIntegrationTests.SmsNotificationEndpoints;

/// <summary>
/// In-memory stand-in for the Twilio provider so integration tests exercise the full flow deterministically
/// without any real network traffic. Records everything it is asked to do so tests can assert on it.
/// </summary>
public sealed class FakeMessagingProvider : IMessagingProvider
{
    public sealed record Record(string Sid, string To, string Body, bool Scheduled)
    {
        public string Status { get; set; } = Scheduled ? "scheduled" : "queued";
        public bool Redacted { get; set; }
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
    }

    private readonly ConcurrentDictionary<string, Record> _bySid = new();
    private int _counter;

    public string SendingNumber => "+15005550006";

    public int SendCount => _bySid.Values.Count(r => !r.Scheduled);
    public int ScheduleCount => _bySid.Values.Count(r => r.Scheduled);
    public IReadOnlyCollection<Record> Records => _bySid.Values.ToList();
    public Record? Get(string sid) => _bySid.TryGetValue(sid, out var r) ? r : null;

    public Task<PhoneValidationResult> ValidateNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Contains("invalid", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PhoneValidationResult.Invalid("TOO_SHORT"));
        }

        // Canonicalize: strip spaces/dashes/parens and ensure a leading '+'.
        var digits = new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
        var canonical = digits.StartsWith('+') ? digits : "+" + digits;
        return Task.FromResult(PhoneValidationResult.Valid(canonical));
    }

    public Task<SentMessage> SendSmsAsync(string toE164, string body, CancellationToken cancellationToken)
    {
        var sid = NextSid();
        _bySid[sid] = new Record(sid, toE164, body, Scheduled: false);
        return Task.FromResult(new SentMessage(sid, "queued"));
    }

    public Task<SentMessage> ScheduleSmsAsync(string toE164, string body, DateTimeOffset sendAtUtc, CancellationToken cancellationToken)
    {
        var sid = NextSid();
        _bySid[sid] = new Record(sid, toE164, body, Scheduled: true);
        return Task.FromResult(new SentMessage(sid, "scheduled"));
    }

    public Task<MessageDeliveryStatus> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        if (_bySid.TryGetValue(providerMessageSid, out var record))
        {
            record.Status = "canceled";
        }
        return Task.FromResult(new MessageDeliveryStatus("canceled", null, null));
    }

    public Task<MessageDeliveryStatus> GetStatusAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var status = _bySid.TryGetValue(providerMessageSid, out var record) ? record.Status : "delivered";
        return Task.FromResult(new MessageDeliveryStatus(status, null, null));
    }

    public Task RedactBodyAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        if (_bySid.TryGetValue(providerMessageSid, out var record))
        {
            record.Redacted = true;
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessage>> ListMessagesAsync(string fromNumber, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderMessage> messages = _bySid.Values
            .Where(r => r.CreatedAt >= fromUtc && r.CreatedAt <= toUtc)
            .Select(r => new ProviderMessage(r.Sid, r.Status, fromNumber, r.To, r.CreatedAt))
            .ToList();
        return Task.FromResult(messages);
    }

    private string NextSid() => "SM" + Interlocked.Increment(ref _counter).ToString("D32");
}
