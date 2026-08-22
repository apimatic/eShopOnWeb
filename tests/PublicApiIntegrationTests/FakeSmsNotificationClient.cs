using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace PublicApiIntegrationTests;

public class FakeSmsNotificationClient : ISmsNotificationClient
{
    private readonly ConcurrentDictionary<string, SmsMessageResult> _messages = new(StringComparer.Ordinal);
    public int SendCount { get; private set; }
    public bool RejectLookups { get; set; }

    public Task<PhoneNumberLookupResult> LookupAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        if (RejectLookups)
        {
            return Task.FromResult(new PhoneNumberLookupResult { IsValid = false, CanonicalNumber = null });
        }

        var canonical = phoneNumber.StartsWith('+') ? phoneNumber : $"+1{phoneNumber}";
        return Task.FromResult(new PhoneNumberLookupResult
        {
            IsValid = true,
            CanonicalNumber = canonical
        });
    }

    public Task<SmsMessageResult> SendAsync(string to, string body, CancellationToken cancellationToken)
        => Store(to, body, "queued");

    public Task<SmsMessageResult> ScheduleAsync(string to, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
        => Store(to, body, "scheduled");

    public Task<SmsMessageResult> CancelScheduledAsync(string providerSid, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue(providerSid, out var existing))
        {
            var updated = Clone(existing, "canceled", existing.Body);
            _messages[providerSid] = updated;
            return Task.FromResult(updated);
        }

        return Task.FromResult(new SmsMessageResult { Sid = providerSid, Status = "canceled" });
    }

    public Task<SmsMessageResult> FetchAsync(string providerSid, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue(providerSid, out var existing))
        {
            return Task.FromResult(existing);
        }

        return Task.FromResult(new SmsMessageResult { Sid = providerSid, Status = "delivered" });
    }

    public Task<SmsMessageResult> RedactBodyAsync(string providerSid, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue(providerSid, out var existing))
        {
            var redacted = Clone(existing, existing.Status, "");
            _messages[providerSid] = redacted;
            return Task.FromResult(redacted);
        }

        return Task.FromResult(new SmsMessageResult { Sid = providerSid, Body = "", Status = "delivered" });
    }

    public Task<SmsReconciliationPage> ListSentFromAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SmsReconciliationPage
        {
            FromNumber = "+15005550006",
            Messages = _messages.Values.ToList(),
            Truncated = false
        });
    }

    private Task<SmsMessageResult> Store(string to, string body, string status)
    {
        SendCount++;
        var sid = $"SM{Guid.NewGuid():N}";
        var result = new SmsMessageResult
        {
            Sid = sid,
            Status = status,
            To = to,
            From = "+15005550006",
            Body = body,
            DateCreated = DateTimeOffset.UtcNow.ToString("r")
        };
        _messages[sid] = result;
        return Task.FromResult(result);
    }

    private static SmsMessageResult Clone(SmsMessageResult existing, string? status, string? body) => new()
    {
        Sid = existing.Sid,
        Status = status,
        To = existing.To,
        From = existing.From,
        Body = body,
        DateCreated = existing.DateCreated,
        DateSent = existing.DateSent,
        DateUpdated = existing.DateUpdated,
        ErrorCode = existing.ErrorCode,
        ErrorMessage = existing.ErrorMessage,
        MessagingServiceSid = existing.MessagingServiceSid
    };
}
