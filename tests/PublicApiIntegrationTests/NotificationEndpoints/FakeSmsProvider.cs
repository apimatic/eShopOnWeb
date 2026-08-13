using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// In-memory stand-in for the SMS provider so notification logic can be exercised without spending a
/// real message. Records what it was asked to do so tests can assert on it.
/// </summary>
public class FakeSmsProvider : ISmsProvider
{
    private int _seq;
    private readonly Dictionary<string, string> _statusBySid = new();

    public int SendCount { get; private set; }
    public List<string> SentBodies { get; } = new();
    public List<string> ScheduledSids { get; } = new();
    public HashSet<string> CanceledSids { get; } = new();
    public HashSet<string> RedactedSids { get; } = new();

    /// <summary>When set, the next (and subsequent) immediate sends throw as if the provider rejected them.</summary>
    public bool ThrowOnSend { get; set; }

    /// <summary>Override validity by raw number; default treats a number containing "invalid" as unusable.</summary>
    public Func<string, bool>? IsUsableOverride { get; set; }

    public Task<PhoneValidationResult> ValidateNumberAsync(string rawNumber, CancellationToken ct = default)
    {
        bool usable = IsUsableOverride?.Invoke(rawNumber) ?? !rawNumber.Contains("invalid");
        return Task.FromResult(new PhoneValidationResult(usable, usable ? Canonicalize(rawNumber) : null));
    }

    public Task<SentMessageResult> SendAsync(string toE164, string body, CancellationToken ct = default)
    {
        if (ThrowOnSend)
        {
            throw new SmsProviderException("simulated provider rejection", 400, true);
        }

        SendCount++;
        SentBodies.Add(body);
        var sid = NextSid();
        _statusBySid[sid] = "queued";
        return Task.FromResult(new SentMessageResult(sid, "queued", null, null));
    }

    public Task<SentMessageResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct = default)
    {
        var sid = NextSid();
        _statusBySid[sid] = "scheduled";
        ScheduledSids.Add(sid);
        return Task.FromResult(new SentMessageResult(sid, "scheduled", null, null));
    }

    public Task<MessageDeliveryState> CancelScheduledAsync(string providerSid, CancellationToken ct = default)
    {
        CanceledSids.Add(providerSid);
        _statusBySid[providerSid] = "canceled";
        return Task.FromResult(new MessageDeliveryState(providerSid, "canceled", null, null, null));
    }

    public Task<MessageDeliveryState> GetMessageStateAsync(string providerSid, CancellationToken ct = default)
    {
        var status = _statusBySid.TryGetValue(providerSid, out var s) ? s : "unknown";
        return Task.FromResult(new MessageDeliveryState(providerSid, status, null, null, null));
    }

    public Task RedactContentAsync(string providerSid, CancellationToken ct = default)
    {
        RedactedSids.Add(providerSid);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessageRecord>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        IReadOnlyList<ProviderMessageRecord> records = _statusBySid
            .Select(kv => new ProviderMessageRecord(kv.Key, "+15145550100", "+15005550006", kv.Value, null))
            .ToList();
        return Task.FromResult(records);
    }

    private string NextSid() => $"SM{(++_seq):D32}";

    private static string Canonicalize(string raw)
    {
        var trimmed = raw.Replace(" ", string.Empty);
        return trimmed.StartsWith("+") ? trimmed : "+" + trimmed;
    }
}
