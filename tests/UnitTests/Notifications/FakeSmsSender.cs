using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.UnitTests.Notifications;

/// <summary>An in-memory <see cref="ISmsSender"/> for orchestration tests — records calls and lets each be scripted.</summary>
internal sealed class FakeSmsSender : ISmsSender
{
    private int _sidSeq;

    public string SendingNumber { get; set; } = "+15550001111";

    public List<(string To, string Body)> Sent { get; } = new();
    public List<(string To, string Body, DateTimeOffset SendAt)> Scheduled { get; } = new();
    public List<string> Canceled { get; } = new();
    public List<string> Redacted { get; } = new();
    public List<string> StatusFetches { get; } = new();

    public Func<string, PhoneNumberValidationResult>? OnValidate { get; set; }
    public Func<string, string, SmsSendResult>? OnSend { get; set; }
    public Func<string, string, DateTimeOffset, SmsSendResult>? OnSchedule { get; set; }
    public Func<string, SmsMessageStatus>? OnFetchStatus { get; set; }
    public Action<string>? OnCancel { get; set; }
    public Action<string>? OnRedact { get; set; }
    public List<ProviderMessageRecord> ProviderMessages { get; } = new();

    public Task<PhoneNumberValidationResult> ValidateAsync(string phoneNumber, CancellationToken cancellationToken)
        => Task.FromResult(OnValidate?.Invoke(phoneNumber)
            ?? new PhoneNumberValidationResult { IsValid = true, CanonicalNumber = phoneNumber });

    public Task<SmsSendResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken)
    {
        Sent.Add((toNumber, body));
        var result = OnSend?.Invoke(toNumber, body) ?? new SmsSendResult { Sid = NextSid(), Status = "queued" };
        return Task.FromResult(result);
    }

    public Task<SmsSendResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken)
    {
        Scheduled.Add((toNumber, body, sendAt));
        var result = OnSchedule?.Invoke(toNumber, body, sendAt) ?? new SmsSendResult { Sid = NextSid(), Status = "scheduled" };
        return Task.FromResult(result);
    }

    public Task CancelScheduledAsync(string messageSid, CancellationToken cancellationToken)
    {
        Canceled.Add(messageSid);
        OnCancel?.Invoke(messageSid);
        return Task.CompletedTask;
    }

    public Task<SmsMessageStatus> FetchStatusAsync(string messageSid, CancellationToken cancellationToken)
    {
        StatusFetches.Add(messageSid);
        return Task.FromResult(OnFetchStatus?.Invoke(messageSid) ?? new SmsMessageStatus { Status = "delivered" });
    }

    public Task RedactBodyAsync(string messageSid, CancellationToken cancellationToken)
    {
        Redacted.Add(messageSid);
        OnRedact?.Invoke(messageSid);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken)
        => Task.FromResult((IReadOnlyList<ProviderMessageRecord>)ProviderMessages);

    private string NextSid() => $"SM{++_sidSeq:D6}";
}
