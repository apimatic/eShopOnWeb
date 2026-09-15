using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests;

public sealed class FakeSmsProvider : ISmsProvider
{
    private readonly object _lock = new();
    private readonly Dictionary<string, ProviderMessageSnapshot> _messages = new(StringComparer.Ordinal);
    private int _nextId;

    private FakeSmsProvider() { }

    public static FakeSmsProvider Instance { get; } = new();
    public bool FailNextImmediate { get; set; }
    public int SendCount { get; private set; }

    public void Reset()
    {
        lock (_lock)
        {
            _messages.Clear();
            _nextId = 0;
            SendCount = 0;
            FailNextImmediate = false;
        }
    }

    public Task<PhoneValidationResult> ValidatePhoneNumberAsync(string input, CancellationToken cancellationToken) =>
        Task.FromResult(new PhoneValidationResult(
            input.StartsWith('+'),
            input.StartsWith('+') ? input : null,
            Array.Empty<string>()));

    public Task<ProviderMessageSnapshot> SendAsync(
        string destination,
        string content,
        DateTimeOffset? sendAt,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            SendCount++;
            var id = $"SM-FAKE-{++_nextId}";
            var failed = sendAt is null && FailNextImmediate;
            FailNextImmediate = false;
            var now = DateTimeOffset.UtcNow;
            var snapshot = new ProviderMessageSnapshot(
                id,
                sendAt.HasValue ? "scheduled" : failed ? "undelivered" : "delivered",
                failed ? 30000 : null,
                failed ? "Simulated carrier rejection." : null,
                now,
                sendAt.HasValue ? null : now,
                now,
                "test-sender",
                destination,
                content,
                sendAt.HasValue ? "test-service" : null);
            _messages.Add(id, snapshot);
            return Task.FromResult(snapshot);
        }
    }

    public Task<ProviderMessageSnapshot> CancelAsync(string providerMessageId, CancellationToken cancellationToken) =>
        Update(providerMessageId, current => current with { Status = "canceled", UpdatedAt = DateTimeOffset.UtcNow });

    public Task<ProviderMessageSnapshot> FetchAsync(string providerMessageId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return Task.FromResult(_messages[providerMessageId]);
        }
    }

    public Task<ProviderMessageSnapshot> DisposeContentAsync(string providerMessageId, CancellationToken cancellationToken) =>
        Update(providerMessageId, current => current with { Body = string.Empty, UpdatedAt = DateTimeOffset.UtcNow });

    public Task<IReadOnlyList<ProviderMessageSnapshot>> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            IReadOnlyList<ProviderMessageSnapshot> result = _messages.Values
                .Where(x => (x.SentAt ?? x.CreatedAt) > from && (x.SentAt ?? x.CreatedAt) < to)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    public ProviderMessageSnapshot Message(string providerMessageId)
    {
        lock (_lock)
        {
            return _messages[providerMessageId];
        }
    }

    private Task<ProviderMessageSnapshot> Update(
        string providerMessageId,
        Func<ProviderMessageSnapshot, ProviderMessageSnapshot> update)
    {
        lock (_lock)
        {
            var updated = update(_messages[providerMessageId]);
            _messages[providerMessageId] = updated;
            return Task.FromResult(updated);
        }
    }
}
