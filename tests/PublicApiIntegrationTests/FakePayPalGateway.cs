using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace PublicApiIntegrationTests;

/// <summary>
/// In-memory stand-in for PayPal used by wiring/ownership/authorization integration tests, so
/// they don't depend on network access or real sandbox credentials. Mimics just enough of
/// PayPal's authorize/capture/refund behavior to exercise the application's own state machine.
/// </summary>
public class FakePayPalGateway : IPayPalGateway
{
    private int _counter;
    private readonly ConcurrentDictionary<string, (decimal Captured, decimal Refunded)> _captures = new();

    public Task<VaultedCardResult> CreatePaymentTokenAsync(CardDetails card, string merchantCustomerId, CancellationToken ct)
    {
        var id = $"VAULT-{Interlocked.Increment(ref _counter)}";
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new VaultedCardResult { VaultId = id, CardBrand = "Visa", Last4 = last4, Expiry = card.Expiry });
    }

    public Task DeletePaymentTokenAsync(string vaultId, CancellationToken ct) => Task.CompletedTask;

    public Task<OrderAuthorizationResult> AuthorizeAsync(decimal amount, string currency, string payPalRequestId, CardDetails? card, string? vaultId, CancellationToken ct)
    {
        var id = $"AUTH-{Interlocked.Increment(ref _counter)}";
        return Task.FromResult(new OrderAuthorizationResult
        {
            PayPalOrderId = $"ORDER-{id}",
            AuthorizationId = id,
            Status = "CREATED",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
        });
    }

    public Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct)
    {
        return Task.FromResult(new ReauthorizationResult { AuthorizationId = authorizationId, Status = "CREATED", ExpiresAt = DateTimeOffset.UtcNow.AddDays(3) });
    }

    public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string payPalRequestId, CancellationToken ct)
    {
        var id = $"CAPTURE-{Interlocked.Increment(ref _counter)}";
        _captures[id] = (amount, 0m);
        var fee = Math.Round(amount * 0.029m + 0.30m, 2);
        return Task.FromResult(new CaptureResult { CaptureId = id, Status = "COMPLETED", CapturedAmount = amount, FeeAmount = fee, NetAmount = amount - fee });
    }

    public Task VoidAsync(string authorizationId, string payPalRequestId, CancellationToken ct) => Task.CompletedTask;

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var refundId = $"REFUND-{Interlocked.Increment(ref _counter)}";
        var state = _captures.GetValueOrDefault(captureId, (Captured: 0m, Refunded: 0m));
        var refundAmount = amount ?? (state.Captured - state.Refunded);
        var totalRefunded = state.Refunded + refundAmount;
        _captures[captureId] = (state.Captured, totalRefunded);

        return Task.FromResult(new RefundResult { RefundId = refundId, Status = "COMPLETED", Amount = refundAmount, TotalRefundedAmount = totalRefunded });
    }

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<PayPalTransactionRecord>>(Array.Empty<PayPalTransactionRecord>());
    }
}
