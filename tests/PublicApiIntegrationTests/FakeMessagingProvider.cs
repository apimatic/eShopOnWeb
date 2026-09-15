using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests;

public sealed class FakeMessagingProvider : IMessagingProvider
{
    private readonly ConcurrentDictionary<string, ProviderMessageState> _messages = new();
    private int _sequence;

    public Task<DestinationValidation> ValidateDestinationAsync(string input, CancellationToken cancellationToken)
    {
        var allowed = new[]
        {
            Environment.GetEnvironmentVariable("TWILIO_TEST_TO_NUMBER"),
            Environment.GetEnvironmentVariable("TWILIO_UNREACHABLE_TO_NUMBER")
        };
        return Task.FromResult(new DestinationValidation(allowed.Contains(input, StringComparer.Ordinal), input));
    }

    public Task<ProviderMessageState> SendAsync(
        string destination,
        string body,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        var sid = $"fake-{Interlocked.Increment(ref _sequence)}";
        var unreachable = string.Equals(
            destination,
            Environment.GetEnvironmentVariable("TWILIO_UNREACHABLE_TO_NUMBER"),
            StringComparison.Ordinal);
        var state = new ProviderMessageState(
            sid,
            sendAt.HasValue ? "scheduled" : unreachable ? "undelivered" : "delivered",
            "integration-test-sender",
            "integration-test-service",
            DateTimeOffset.UtcNow.ToString("O"),
            sendAt.HasValue ? null : DateTimeOffset.UtcNow.ToString("O"),
            DateTimeOffset.UtcNow.ToString("O"),
            unreachable ? 30000 : null,
            unreachable ? "Test destination is unreachable." : null,
            body);
        _messages[sid] = state;
        return Task.FromResult(state);
    }

    public Task<ProviderMessageState> FetchAsync(string providerMessageSid, CancellationToken cancellationToken) =>
        Task.FromResult(_messages[providerMessageSid]);

    public Task<ProviderMessageState> CancelScheduledAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var state = _messages[providerMessageSid] with
        {
            Status = "canceled",
            DateUpdated = DateTimeOffset.UtcNow.ToString("O")
        };
        _messages[providerMessageSid] = state;
        return Task.FromResult(state);
    }

    public Task<ProviderMessageState> DisposeContentAsync(string providerMessageSid, CancellationToken cancellationToken)
    {
        var state = _messages[providerMessageSid] with
        {
            Body = null,
            DateUpdated = DateTimeOffset.UtcNow.ToString("O")
        };
        _messages[providerMessageSid] = state;
        return Task.FromResult(state);
    }

    public Task<IReadOnlyList<ProviderMessageState>> ListSentAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderMessageState> result = _messages.Values
            .Where(x => DateTimeOffset.TryParse(x.DateSent, out var sent) && sent >= from && sent <= to)
            .ToList();
        return Task.FromResult(result);
    }

    public bool IsContentDisposed(string sid) => _messages.TryGetValue(sid, out var value) && value.Body is null;
}
