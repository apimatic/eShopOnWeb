using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests;

public sealed class FakeSmsProvider : ISmsProvider
{
    private readonly object _gate = new();
    private readonly List<SmsMessageSnapshot> _messages = new();
    private int _nextSid;
    public bool RejectSends { get; set; }

    public int SendCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    public Task<PhoneNumberValidationResult> ValidatePhoneNumberAsync(
        string phoneNumber,
        string? countryCode,
        CancellationToken cancellationToken)
    {
        var valid = !string.IsNullOrWhiteSpace(phoneNumber);
        return Task.FromResult(new PhoneNumberValidationResult(
            valid,
            valid ? phoneNumber : null,
            valid ? Array.Empty<string>() : new[] { "NOT_A_NUMBER" }));
    }

    public Task<SmsMessageSnapshot> SendMessageAsync(
        string e164Destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        if (RejectSends)
        {
            throw new SmsProviderException("fake message creation", 21610);
        }

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var message = new SmsMessageSnapshot(
                $"SM{++_nextSid:D32}",
                sendAt.HasValue ? "scheduled" : "undelivered",
                sendAt.HasValue ? null : 30005,
                now,
                now,
                sendAt.HasValue ? null : now);
            _messages.Add(message);
            return Task.FromResult(message);
        }
    }

    public Task<SmsMessageSnapshot> GetMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_messages.Single(x => x.Sid == messageSid));
        }
    }

    public Task<SmsMessageSnapshot> CancelMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        return Update(messageSid, x => x with { Status = "canceled", DateUpdated = DateTimeOffset.UtcNow });
    }

    public Task<SmsMessageSnapshot> RedactMessageAsync(string messageSid, CancellationToken cancellationToken)
    {
        return Update(messageSid, x => x with { DateUpdated = DateTimeOffset.UtcNow });
    }

    public Task<IReadOnlyList<SmsMessageSnapshot>> ListMessagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<SmsMessageSnapshot>>(_messages
                .Where(x => (x.DateSent ?? x.DateCreated) >= from && (x.DateSent ?? x.DateCreated) <= to)
                .ToArray());
        }
    }

    private Task<SmsMessageSnapshot> Update(string sid, Func<SmsMessageSnapshot, SmsMessageSnapshot> update)
    {
        lock (_gate)
        {
            var index = _messages.FindIndex(x => x.Sid == sid);
            var updated = update(_messages[index]);
            _messages[index] = updated;
            return Task.FromResult(updated);
        }
    }
}
