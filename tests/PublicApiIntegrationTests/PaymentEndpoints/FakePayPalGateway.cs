using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// A deterministic in-memory stand-in for the PayPal gateway so endpoint/service behaviour (state
/// machine, idempotency, ownership, refund caps) can be tested without touching the network.
/// </summary>
public class FakePayPalGateway : IPayPalGateway
{
    private int _seq;

    public int AuthorizeCalls { get; private set; }
    public int CaptureCalls { get; private set; }
    public int VoidCalls { get; private set; }
    public int RefundCalls { get; private set; }
    public int VaultCalls { get; private set; }
    public int DeleteCalls { get; private set; }
    public List<PayPalTransactionRecord> Transactions { get; } = new();

    public Task<AuthorizationResult> AuthorizeAsync(decimal amount, string correlationId, CardPaymentInstrument instrument, string idempotencyKey, CancellationToken ct)
    {
        AuthorizeCalls++;
        var n = ++_seq;
        return Task.FromResult(new AuthorizationResult($"PPORDER{n}", $"AUTH{n}", "CREATED", amount, "USD"));
    }

    public Task<AuthorizationRenewalResult> EnsureCapturableAsync(string authorizationId, decimal amount, string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(new AuthorizationRenewalResult(false, authorizationId, true, null));

    public Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string correlationId, string idempotencyKey, CancellationToken ct)
    {
        CaptureCalls++;
        var fee = Math.Round(amount * 0.029m + 0.30m, 2);
        return Task.FromResult(new CaptureResult($"CAP{++_seq}", "COMPLETED", amount, fee, amount - fee, "USD"));
    }

    public Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        VoidCalls++;
        return Task.CompletedTask;
    }

    public Task<RefundResult> RefundAsync(string captureId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        RefundCalls++;
        return Task.FromResult(new RefundResult($"REF{++_seq}", "COMPLETED", amount ?? 0m, "USD"));
    }

    public Task<SavedCardResult> VaultCardAsync(string merchantCustomerId, CardDetails card, string idempotencyKey, CancellationToken ct)
    {
        VaultCalls++;
        var n = ++_seq;
        var last4 = card.Number.Length >= 4 ? card.Number[^4..] : card.Number;
        return Task.FromResult(new SavedCardResult($"VAULT{n}", merchantCustomerId, "VISA", last4, card.Expiry));
    }

    public Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        DeleteCalls++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PayPalTransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PayPalTransactionRecord>>(Transactions);
}
