using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <see cref="OrderAggregate.Order"/>. There is one OrderPayment per order.
/// It carries enough of the state PayPal owns — the ids and current status for the hold, the
/// capture, and each refund — that a later request can act on it, not only the one that created it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.Now;
        IdempotencyToken = Guid.NewGuid();
    }

    public int OrderId { get; private set; }

    /// <summary>
    /// A stable, globally-unique token minted when the payment is created. PayPal-Request-Id values for
    /// this order's authorize/capture/void are derived from it, so a double-click is idempotent while two
    /// different orders never collide.
    /// </summary>
    public Guid IdempotencyToken { get; private set; }

    /// <summary>The owning shopper's identity (matches <see cref="OrderAggregate.Order.BuyerId"/>).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total — the amount held to the cent at authorization.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>How the shopper paid, safe to display (e.g. "VISA ****1111"). Never full card details.</summary>
    public string? PaymentMethodDescriptor { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Refunds ---
    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, string? descriptor)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        PaymentMethodDescriptor = descriptor;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed authorization id/status after a stale hold was re-authorized.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    public void SetCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. Guards that a partly-refunded order never becomes refundable beyond what was
    /// captured, and transitions the status to PartiallyRefunded / Refunded.
    /// </summary>
    public PaymentRefund AddRefund(string payPalRefundId, decimal amount, string idempotencyKey, string status)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException("Only a captured payment can be refunded.");

        if (amount > RefundableRemaining())
            throw new InvalidOperationException("Refund amount exceeds the remaining refundable balance.");

        var refund = new PaymentRefund(payPalRefundId, amount, idempotencyKey, status);
        _refunds.Add(refund);

        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
