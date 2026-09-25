using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the money-movement and fulfilment state that follows a placed <see cref="OrderAggregate.Order"/>:
/// the PayPal ids and statuses for the hold (authorization), the capture, and any refunds. One
/// <see cref="OrderPayment"/> exists per order; it is created (AwaitingPayment) when the order is placed.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Stable external reference stamped on the PayPal order/capture and used for reconciliation.</summary>
    public string InvoiceId { get; private set; }

    public PaymentState State { get; private set; } = PaymentState.AwaitingPayment;

    // PayPal-owned state — enough that a later request can act on the payment, not only the one that started it.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Operator-actionable description of the last failure (e.g. an authorization that can no longer be renewed).</summary>
    public string? LastError { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceId = invoiceId;
    }

    public void SetPayPalOrderId(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        State = PaymentState.Authorized;
        LastError = null;
    }

    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        State = PaymentState.Fulfilled;
        AuthorizationStatus = "CAPTURED";
        LastError = null;
    }

    public void MarkCancelled()
    {
        State = PaymentState.Cancelled;
        AuthorizationStatus = "VOIDED";
        LastError = null;
    }

    public void SetError(string message) => LastError = message;

    public PaymentRefund AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
        return refund;
    }

    public void RecomputeRefundState()
    {
        var refunded = RefundedAmount();
        if (refunded <= 0m) return;
        State = refunded >= (CapturedAmount ?? Amount)
            ? PaymentState.Refunded
            : PaymentState.PartiallyRefunded;
        CaptureStatus = State == PaymentState.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
    }

    /// <summary>Sum of refunds that still count against the capture (not failed/cancelled).</summary>
    public decimal RefundedAmount() => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the captured payment can still be refunded — never more than was captured.</summary>
    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - RefundedAmount();
}
