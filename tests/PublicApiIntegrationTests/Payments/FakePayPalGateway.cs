using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// A deterministic in-memory stand-in for PayPal, used to drive the endpoint flows without a network. It
/// mirrors the money lifecycle (hold → capture with fee/net → refund) faithfully enough to assert on.
/// </summary>
public sealed class FakePayPalGateway : IPayPalGateway
{
    private int _counter;
    private readonly ConcurrentDictionary<string, decimal> _authorizedAmounts = new();
    private readonly ConcurrentDictionary<string, decimal> _capturedAmounts = new();

    public bool NextAuthorizationIsStale { get; set; }

    private string NextId(string prefix) => $"{prefix}-{Interlocked.Increment(ref _counter)}";

    public Task<AuthorizationResult> AuthorizeWithCardAsync(decimal amount, string currency, CardDetails card,
        string idempotencyKey, CancellationToken ct) => Authorize(amount);

    public Task<AuthorizationResult> AuthorizeWithVaultedCardAsync(decimal amount, string currency, string vaultId,
        string idempotencyKey, CancellationToken ct) => Authorize(amount);

    private Task<AuthorizationResult> Authorize(decimal amount)
    {
        var authId = NextId("AUTH");
        _authorizedAmounts[authId] = amount;
        var expiry = NextAuthorizationIsStale ? DateTimeOffset.Now.AddDays(-1) : DateTimeOffset.Now.AddDays(3);
        return Task.FromResult(new AuthorizationResult(NextId("PPO"), authId, "CREATED", expiry));
    }

    public Task<AuthorizationState> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var expiry = NextAuthorizationIsStale ? DateTimeOffset.Now.AddDays(-1) : DateTimeOffset.Now.AddDays(3);
        return Task.FromResult(new AuthorizationState("CREATED", expiry));
    }

    public Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency,
        CancellationToken ct)
    {
        var authId = NextId("AUTH");
        _authorizedAmounts[authId] = amount;
        NextAuthorizationIsStale = false;
        return Task.FromResult(new ReauthorizationResult(authId, "CREATED", DateTimeOffset.Now.AddDays(3)));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        var amount = _authorizedAmounts.TryGetValue(authorizationId, out var a) ? a : 0m;
        var captureId = NextId("CAP");
        _capturedAmounts[captureId] = amount;
        var fee = Math.Round(amount * 0.029m + 0.30m, 2);
        return Task.FromResult(new CaptureResult(captureId, "COMPLETED", amount, fee, amount - fee, "USD"));
    }

    public Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct) => Task.CompletedTask;

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken ct)
    {
        var refunded = amount ?? (_capturedAmounts.TryGetValue(captureId, out var c) ? c : 0m);
        return Task.FromResult(new RefundResult(NextId("REF"), "COMPLETED", refunded, currency));
    }

    public Task<VaultedCardResult> VaultCardAsync(CardDetails card, string customerReference, CancellationToken ct)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        var lastFour = digits.Length >= 4 ? digits[^4..] : digits;
        return Task.FromResult(new VaultedCardResult(NextId("VAULT"), "Visa", lastFour, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct) => Task.CompletedTask;

    public Task<IReadOnlyList<ReconciliationTransaction>> SearchTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken ct)
    {
        IReadOnlyList<ReconciliationTransaction> transactions = _capturedAmounts
            .Select(kvp => new ReconciliationTransaction(kvp.Key, "S", kvp.Value, "USD", DateTimeOffset.Now))
            .ToList();
        return Task.FromResult(transactions);
    }
}
