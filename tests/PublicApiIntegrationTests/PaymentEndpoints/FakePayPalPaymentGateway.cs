using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Test double for the PayPal gateway — the seam that keeps integration tests off the network. It
/// records how many real charges/refunds it performed (de-duplicating by idempotency key, exactly
/// as PayPal's PayPal-Request-Id does) so tests can assert that a double-click charges only once.
/// </summary>
public class FakePayPalPaymentGateway : IPayPalPaymentGateway
{
    private readonly Dictionary<string, PaymentCaptureResult> _charges = new();
    private readonly Dictionary<string, RefundResult> _refunds = new();

    public int ChargeCount { get; private set; }
    public int VaultCount { get; private set; }
    public int RefundCount { get; private set; }

    public Task<PaymentCaptureResult> ChargeCardAsync(decimal amount, string currencyCode, CardDetails card,
        string idempotencyKey, CancellationToken cancellationToken = default) => Charge(idempotencyKey);

    public Task<PaymentCaptureResult> ChargeVaultedCardAsync(decimal amount, string currencyCode, string vaultId,
        string idempotencyKey, CancellationToken cancellationToken = default) => Charge(idempotencyKey);

    private Task<PaymentCaptureResult> Charge(string idempotencyKey)
    {
        if (!_charges.TryGetValue(idempotencyKey, out var result))
        {
            ChargeCount++;
            result = new PaymentCaptureResult(NewId("PPO"), NewId("CAP"), "COMPLETED");
            _charges[idempotencyKey] = result;
        }
        return Task.FromResult(result);
    }

    public Task<VaultedCardResult> VaultCardAsync(CardDetails card, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        VaultCount++;
        return Task.FromResult(new VaultedCardResult(NewId("VAULT"), "VISA", card.Last4, card.Expiry));
    }

    public Task<RefundResult> RefundCaptureAsync(string captureId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!_refunds.TryGetValue(idempotencyKey, out var result))
        {
            RefundCount++;
            result = new RefundResult(NewId("REF"), "COMPLETED");
            _refunds[idempotencyKey] = result;
        }
        return Task.FromResult(result);
    }

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..12];
}
