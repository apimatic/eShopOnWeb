using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// A stateful in-memory stand-in for the PayPal gateway, so the endpoints and payment services can be
/// exercised end-to-end without hitting the network. It mirrors the parts of PayPal's behaviour the
/// domain depends on (fee/net breakdown on capture, refund amounts, vaulting).
/// </summary>
public class FakePayPalPaymentService : IPayPalPaymentService
{
    private int _seq;
    private readonly ConcurrentDictionary<string, decimal> _authorizedAmounts = new();

    public int AuthorizeCallCount { get; private set; }
    public int CaptureCallCount { get; private set; }
    public List<string> VoidedAuthorizations { get; } = new();
    public List<string> DeletedVaults { get; } = new();
    public List<PayPalTransactionRecord> Transactions { get; set; } = new();

    private int Next() => Interlocked.Increment(ref _seq);

    public Task<PayPalAuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, PayPalPaymentSource source, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        AuthorizeCallCount++;
        var authId = $"AUTH-{Next()}";
        _authorizedAmounts[authId] = amount;
        return Task.FromResult(new PayPalAuthorizationResult($"PPO-{Next()}", authId, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task<PayPalCaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        CaptureCallCount++;
        var amount = _authorizedAmounts.TryGetValue(authorizationId, out var a) ? a : 0m;
        var fee = Math.Round(amount * 0.03m, 2);
        return Task.FromResult(new PayPalCaptureResult($"CAP-{Next()}", "COMPLETED", amount, fee, amount - fee, "USD"));
    }

    public Task<PayPalReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PayPalReauthorizationResult(authorizationId, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        VoidedAuthorizations.Add(authorizationId);
        return Task.CompletedTask;
    }

    public Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode, string idempotencyKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(new PayPalRefundResult($"REF-{Next()}", "COMPLETED", amount ?? 0m));

    public Task<PayPalVaultedCard> VaultCardAsync(CardDetails card, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new PayPalVaultedCard($"VAULT-{Next()}", "VISA", last4, card.Expiry, card.CardholderName));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken cancellationToken = default)
    {
        DeletedVaults.Add(vaultId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PayPalTransactionRecord>>(Transactions);
}
