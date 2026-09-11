using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state for a single <see cref="OrderAggregate.Order"/>. Carries enough of the state
/// PayPal owns (order/authorization/capture/refund ids and their current status) that a later
/// request can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.UtcNow;

        // Deterministic idempotency keys reused across retries so a double-click never
        // authorizes or captures twice on the PayPal side.
        AuthorizationRequestId = Guid.NewGuid().ToString("N");
        CaptureRequestId = Guid.NewGuid().ToString("N");

        // Unique external reference threaded through PayPal so the reconciliation report can
        // line a PayPal transaction back up against this order.
        InvoiceId = $"eshop-{orderId}-{Guid.NewGuid():N}";
    }

    public int OrderId { get; private set; }

    public string BuyerId { get; private set; }

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public string InvoiceId { get; private set; }

    public string AuthorizationRequestId { get; private set; }

    public string CaptureRequestId { get; private set; }

    // --- PayPal-owned identifiers / state ---
    public string? PayPalOrderId { get; private set; }

    public string? AuthorizationId { get; private set; }

    public string? CaptureId { get; private set; }

    public decimal? CapturedGross { get; private set; }

    public decimal? PayPalFee { get; private set; }

    public decimal? NetAmount { get; private set; }

    /// <summary>Human-safe description of the card used, e.g. "VISA ****1111". Never full details.</summary>
    public string? CardDescriptor { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    /// <summary>Operator-actionable message when a step needs manual attention.</summary>
    public string? OperatorMessage { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value of refunds that have not failed.</summary>
    public decimal TotalRefunded => _refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.Amount);

    /// <summary>Amount of a capture still available to refund.</summary>
    public decimal RefundableRemaining => (CapturedGross ?? 0m) - TotalRefunded;

    public bool IsAuthorized => AuthorizationId is not null;

    public bool IsCaptured => CaptureId is not null;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? cardDescriptor, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        CardDescriptor = cardDescriptor;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorized;
        OperatorMessage = null;
    }

    /// <summary>A stale authorization was renewed; subsequent capture must target the new id.</summary>
    public void RenewAuthorization(string newAuthorizationId)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
        OperatorMessage = null;
    }

    public void MarkFulfilled(string captureId, decimal capturedGross, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
        OperatorMessage = null;
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        OperatorMessage = null;
    }

    public void SetOperatorMessage(string message)
    {
        OperatorMessage = message;
    }

    /// <summary>
    /// Registers a pending refund after validating it against the refundable remaining. A repeat
    /// request under a previously-seen idempotency key returns the existing refund unchanged.
    /// </summary>
    public Refund AddRefund(string idempotencyKey, decimal? amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        if (!IsCaptured)
        {
            throw new InvalidOperationException("Cannot refund an order that has not been fulfilled (captured).");
        }

        var existing = _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? RefundableRemaining;

        if (refundAmount <= 0m)
        {
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        }

        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > RefundableRemaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refundAmount:F2} exceeds the refundable remaining of {RefundableRemaining:F2} {CurrencyCode}.");
        }

        var refund = new Refund(idempotencyKey, refundAmount, CurrencyCode);
        _refunds.Add(refund);
        return refund;
    }

    public void ApplyRefundResult(Refund refund, string payPalRefundId)
    {
        refund.MarkCompleted(payPalRefundId);
        Status = TotalRefunded >= (CapturedGross ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    public void RemoveRefund(Refund refund)
    {
        _refunds.Remove(refund);
    }
}
