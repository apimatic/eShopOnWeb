using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

namespace PublicApiIntegrationTests.Payments;

/// <summary>
/// A deterministic in-memory stand-in for the PayPal gateway so the payment endpoints can be tested
/// without calling the provider. Records call counts so tests can assert idempotency (that a repeated
/// pay/refund does not reach the gateway a second time), and can be told to simulate a decline.
/// </summary>
public class FakePaymentGateway : IPaymentGateway
{
    private int _sequence;

    public int ChargeCardCalls;
    public int ChargeSavedCardCalls;
    public int VaultCalls;
    public int RefundCalls;

    /// <summary>When set, the next raw-card charge fails as a caller-actionable rejection.</summary>
    public string? DeclineReason { get; set; }

    public Task<PaymentResult> ChargeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ChargeCardCalls);
        if (DeclineReason is not null)
        {
            throw new PaymentException(DeclineReason, PaymentFailureKind.Rejected);
        }

        var n = Interlocked.Increment(ref _sequence);
        return Task.FromResult(new PaymentResult($"PP-ORDER-{n}", $"PP-CAPTURE-{n}", "COMPLETED"));
    }

    public Task<PaymentResult> ChargeSavedCardAsync(SavedCardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref ChargeSavedCardCalls);
        var n = Interlocked.Increment(ref _sequence);
        return Task.FromResult(new PaymentResult($"PP-ORDER-{n}", $"PP-CAPTURE-{n}", "COMPLETED"));
    }

    public Task<VaultedCard> VaultCardAsync(VaultCardRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref VaultCalls);
        var n = Interlocked.Increment(ref _sequence);
        var last4 = request.Card.Number.Length >= 4 ? request.Card.Number[^4..] : request.Card.Number;
        var expiry = $"{request.Card.ExpiryYear:D4}-{request.Card.ExpiryMonth:D2}";
        return Task.FromResult(new VaultedCard($"VAULT-TOKEN-{n}", "VISA", last4, expiry));
    }

    public Task<RefundResult> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref RefundCalls);
        var n = Interlocked.Increment(ref _sequence);
        return Task.FromResult(new RefundResult($"PP-REFUND-{n}", "COMPLETED"));
    }
}
