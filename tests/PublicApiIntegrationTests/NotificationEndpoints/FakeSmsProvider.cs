using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// An in-memory <see cref="ISmsProvider"/> for endpoint tests — no live Twilio traffic. Records what was
/// asked of it and lets each test steer the outcomes.
/// </summary>
public class FakeSmsProvider : ISmsProvider
{
    private int _sid;
    private readonly object _lock = new();

    public bool LookupValid { get; set; } = true;
    public string CanonicalNumber { get; set; } = "+14165550100";

    /// <summary>Return an exception to make the next send/schedule fail, or null to succeed.</summary>
    public Func<Exception?> SendFault { get; set; } = () => null;

    public int SendCount { get; private set; }
    public int ScheduleCount { get; private set; }
    public int CancelCount { get; private set; }
    public int RedactCount { get; private set; }
    public List<string> SentBodies { get; } = new();
    public List<string> CanceledSids { get; } = new();
    public List<string> RedactedSids { get; } = new();

    public string ConfiguredSenderNumber => "+15005550006";

    private string NextSid(string prefix)
    {
        lock (_lock)
        {
            return $"{prefix}{++_sid:D6}";
        }
    }

    public Task<PhoneNumberLookupResult> LookupAsync(string rawNumber, CancellationToken cancellationToken = default) =>
        Task.FromResult(LookupValid
            ? new PhoneNumberLookupResult(true, CanonicalNumber, Array.Empty<string>())
            : new PhoneNumberLookupResult(false, null, new[] { "NOT_A_NUMBER" }));

    public Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        SendCount++;
        var fault = SendFault();
        if (fault is not null)
        {
            return Task.FromException<SmsDispatchResult>(fault);
        }
        SentBodies.Add(body);
        return Task.FromResult(new SmsDispatchResult(NextSid("SM"), "queued", null));
    }

    public Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        ScheduleCount++;
        var fault = SendFault();
        if (fault is not null)
        {
            return Task.FromException<SmsDispatchResult>(fault);
        }
        return Task.FromResult(new SmsDispatchResult(NextSid("SM"), "scheduled", null));
    }

    // Return null = "no change" so tests assert the status recorded at send time deterministically.
    public Task<SmsMessageState?> FetchAsync(string providerMessageSid, CancellationToken cancellationToken = default) =>
        Task.FromResult<SmsMessageState?>(null);

    public Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        CancelCount++;
        CanceledSids.Add(providerMessageSid);
        return Task.CompletedTask;
    }

    public Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        RedactCount++;
        RedactedSids.Add(providerMessageSid);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SmsMessageState>> ListSentFromConfiguredSenderAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SmsMessageState>>(Array.Empty<SmsMessageState>());
}
