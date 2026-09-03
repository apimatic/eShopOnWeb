using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// In-memory stand-in for the PayPal gateway so the endpoints, orchestration, auth and ownership
/// logic can be tested without touching PayPal. It simulates holds, captures, refunds and vaulting,
/// and records settled transactions so reconciliation can be exercised.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, decimal> _authorizedAmounts = new();
    private readonly ConcurrentDictionary<string, (decimal amount, string currency)> _captures = new();
    private readonly List<PayPalTransaction> _transactions = new();
    private readonly object _sync = new();

    public int AuthorizeCallCount;
    public int CaptureCallCount;
    public int RefundCallCount;
    public int VoidCallCount;
    public bool NextAuthorizeRequiresChallenge;
    public bool NextAuthorizeExpiresImmediately;

    public Task<AuthorizationResult> AuthorizeAsync(decimal amount, string currencyCode, CardDetails? card,
        string? vaultId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref AuthorizeCallCount);

        if (NextAuthorizeRequiresChallenge)
            throw new PaymentChallengeRequiredException("Simulated 3-D Secure challenge.");

        var orderId = "PAYPAL-ORDER-" + Guid.NewGuid().ToString("N")[..12];
        var authorizationId = "AUTH-" + Guid.NewGuid().ToString("N")[..12];
        _authorizedAmounts[authorizationId] = amount;

        var expiresAt = NextAuthorizeExpiresImmediately
            ? DateTimeOffset.UtcNow.AddMinutes(-5)
            : DateTimeOffset.UtcNow.AddDays(3);

        return Task.FromResult(new AuthorizationResult(orderId, authorizationId, "CREATED", expiresAt));
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref CaptureCallCount);

        var gross = _authorizedAmounts.TryGetValue(authorizationId, out var amount) ? amount : 0m;
        var fee = Math.Round(gross * 0.029m + 0.30m, 2, MidpointRounding.AwayFromZero);
        var net = gross - fee;
        var captureId = "CAPTURE-" + Guid.NewGuid().ToString("N")[..12];

        _captures[captureId] = (gross, "USD");
        Record(captureId, "COMPLETED", gross, "USD");

        return Task.FromResult(new CaptureResult(captureId, "COMPLETED", gross, fee, net, "USD"));
    }

    public Task<AuthorizationState> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthorizationState("CREATED", DateTimeOffset.UtcNow.AddDays(3)));

    public Task<AuthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var newId = "AUTH-" + Guid.NewGuid().ToString("N")[..12];
        var amount = _authorizedAmounts.TryGetValue(authorizationId, out var a) ? a : 0m;
        _authorizedAmounts[newId] = amount;
        return Task.FromResult(new AuthorizationResult(string.Empty, newId, "CREATED", DateTimeOffset.UtcNow.AddDays(3)));
    }

    public Task VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref VoidCallCount);
        _authorizedAmounts.TryRemove(authorizationId, out _);
        return Task.CompletedTask;
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currencyCode,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RefundCallCount);

        var refundAmount = amount ?? (_captures.TryGetValue(captureId, out var c) ? c.amount : 0m);
        var refundId = "REFUND-" + Guid.NewGuid().ToString("N")[..12];
        Record(refundId, "COMPLETED", refundAmount, currencyCode);

        return Task.FromResult(new RefundResult(refundId, "COMPLETED", refundAmount, currencyCode));
    }

    public Task<VaultedCard> VaultCardAsync(CardDetails card, string? existingCustomerId,
        string merchantCustomerId, CancellationToken cancellationToken = default)
    {
        var tokenId = "VAULT-" + Guid.NewGuid().ToString("N")[..12];
        var customerId = existingCustomerId ?? "CUST-" + Guid.NewGuid().ToString("N")[..10];
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new VaultedCard(tokenId, customerId, "VISA", last4, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string tokenId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<PayPalTransaction> result = _transactions
                .Where(t => t.Date is null || (t.Date >= from && t.Date <= to))
                .ToList();
            return Task.FromResult(result);
        }
    }

    private void Record(string transactionId, string status, decimal amount, string currency)
    {
        lock (_sync)
        {
            _transactions.Add(new PayPalTransaction(transactionId, status, amount, currency, DateTimeOffset.UtcNow));
        }
    }
}
