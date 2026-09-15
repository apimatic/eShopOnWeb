using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Holds the money-movement state for a single <see cref="OrderAggregate.Order"/>. It carries enough of
/// the state PayPal owns — the hold, the capture and the refunds, each with its id and current status — that
/// a later request (fulfil / cancel / refund) can act on it, not only the request that started it.
///
/// This is a separate aggregate joined to the order by <see cref="OrderId"/>, keeping the existing order model
/// untouched. One order has exactly one payment.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    /// <summary>The eShop order this payment is for.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (the order's BuyerId — the username/email). Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>The order total captured at placement time. The PayPal hold must equal this to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Our unique reference sent to PayPal as invoice_id, used to reconcile PayPal records back to orders.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>Idempotency seed used to make authorize/capture safe against a double-click.</summary>
    public string RequestId { get; private set; }

    // PayPal-owned identifiers and status for the hold.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // PayPal-owned identifiers and financials for the capture.
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId, string requestId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(requestId, nameof(requestId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceId = invoiceId;
        RequestId = requestId;
        Status = PaymentStatus.AwaitingPayment;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Refreshes the authorization after a reauthorize produced a new hold (new id / expiry).</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkAuthorizationFailed()
    {
        Status = PaymentStatus.AuthorizationFailed;
        Touch();
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    /// <summary>Sum of all refunds that still count against the capture (everything except failed/cancelled).</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsAgainstCapture).Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }

    /// <summary>Registers a not-yet-confirmed refund. Guards that the order is captured and not over-refunded.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }
        if (refund.Amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the refundable remaining balance of {RefundableRemaining()}.");
        }
        _refunds.Add(refund);
        Touch();
    }

    /// <summary>Recomputes Captured / PartiallyRefunded / Refunded after a refund result is known.</summary>
    public void RecalculateRefundStatus()
    {
        var refunded = TotalRefunded();
        var captured = CapturedAmount ?? 0m;
        if (refunded <= 0m)
        {
            Status = PaymentStatus.Captured;
        }
        else if (refunded >= captured)
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
        Touch();
    }

    public bool IsFulfilled =>
        Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded;
}
