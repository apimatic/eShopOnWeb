using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace PublicApiIntegrationTests.NotificationEndpoints;

/// <summary>
/// An in-memory <see cref="ISmsGateway"/> that records what the integration asked of the provider,
/// so tests can assert behaviour without sending real messages.
/// </summary>
public class FakeSmsGateway : ISmsGateway
{
    private int _counter;
    private readonly object _lock = new();

    public string SenderNumber { get; set; } = "+15551230000";

    public HashSet<string> InvalidNumbers { get; } = new();
    public bool FailSends { get; set; }

    public List<(string To, string Body)> Sends { get; } = new();
    public List<(string To, string Body, DateTimeOffset SendAt)> Schedules { get; } = new();
    public List<string> Canceled { get; } = new();
    public List<string> Redacted { get; } = new();
    public Dictionary<string, string> StatusesBySid { get; } = new();
    public List<ProviderMessage> ProviderMessages { get; } = new();

    public Task<PhoneNumberValidationResult> ValidateDestinationAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (InvalidNumbers.Contains(phoneNumber) || !phoneNumber.StartsWith("+"))
            return Task.FromResult(PhoneNumberValidationResult.Invalid());

        var canonical = phoneNumber.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        return Task.FromResult(PhoneNumberValidationResult.Valid(canonical));
    }

    public Task<SmsDispatchResult> SendAsync(string toNumber, string body, CancellationToken cancellationToken = default)
    {
        if (FailSends)
            throw new SmsGatewayException(HttpStatusCode.BadRequest, 21211, "https://www.twilio.com/docs/errors/21211");

        var sid = NextSid();
        lock (_lock)
        {
            Sends.Add((toNumber, body));
            StatusesBySid[sid] = "queued";
        }
        return Task.FromResult(new SmsDispatchResult { Sid = sid, Status = "queued" });
    }

    public Task<SmsDispatchResult> ScheduleAsync(string toNumber, string body, DateTimeOffset sendAt, CancellationToken cancellationToken = default)
    {
        var sid = NextSid();
        lock (_lock)
        {
            Schedules.Add((toNumber, body, sendAt));
            StatusesBySid[sid] = "scheduled";
        }
        return Task.FromResult(new SmsDispatchResult { Sid = sid, Status = "scheduled" });
    }

    public Task CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Canceled.Add(providerMessageSid);
            StatusesBySid[providerMessageSid] = "canceled";
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetDeliveryStatusAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult(StatusesBySid.TryGetValue(providerMessageSid, out var status) ? status : null);
        }
    }

    public Task RedactContentAsync(string providerMessageSid, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            Redacted.Add(providerMessageSid);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProviderMessage>> ListSentMessagesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult((IReadOnlyList<ProviderMessage>)new List<ProviderMessage>(ProviderMessages));
        }
    }

    private string NextSid()
    {
        lock (_lock)
        {
            return $"SM{(++_counter):D8}00000000000000000000000";
        }
    }
}
