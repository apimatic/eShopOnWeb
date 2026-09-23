using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries all the PayPal-owned payment state for one <see cref="OrderAggregate.Order"/> — the ids and
/// current status of the hold (authorization), the capture, and the refunds — so a later request can act
/// on it. One payment per order: <see cref="OrderId"/> is unique, which is also how a double-clicked
/// "pay" is rejected under concurrency.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    /// <summary>
    /// Claims the payment for an order before the PayPal call is made. Starts in
    /// <see cref="OrderPaymentStatus.Authorizing"/>.
    /// </summary>
    public OrderPayment(int orderId, string buyerId, decimal amount, string currency, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        InvoiceId = invoiceId;
        Status = OrderPaymentStatus.Authorizing;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The eShop order this payment belongs to (unique).</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order (for authorization scoping).</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total the hold/capture is for.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency code from configuration.</summary>
    public string Currency { get; private set; }

    /// <summary>Unique per-order invoice id, used as the reconciliation match key against PayPal.</summary>
    public string InvoiceId { get; private set; }

    public OrderPaymentStatus Status { get; private set; }

    // ----- PayPal-owned identifiers & statuses -----
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>The PayPal event time (auth/capture create_time) — the clock reconciliation filters on.</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? FailureReason { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The PayPal order id has been created; record it before authorization completes.</summary>
    public void RecordPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    /// <summary>Records a successful authorization (a hold on the money).</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, DateTimeOffset? processedAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        ProcessedAt = processedAt;
        Status = OrderPaymentStatus.Authorized;
        FailureReason = null;
    }

    /// <summary>Replaces the authorization id after a re-authorization renewed a stale hold.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Records the capture at fulfilment, including what PayPal reported for fee and net proceeds.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset? processedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        ProcessedAt = processedAt;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
        FailureReason = null;
    }

    /// <summary>Records that the hold was released (a void before fulfilment).</summary>
    public void RecordCancellation()
    {
        AuthorizationStatus = "VOIDED";
        Status = OrderPaymentStatus.Cancelled;
    }

    public void MarkFailed(string reason)
    {
        Status = OrderPaymentStatus.Failed;
        FailureReason = reason;
    }

    /// <summary>The sum of refunds that still count against the captured amount (not failed/cancelled).</summary>
    public decimal TotalRefunded() => _refunds.Where(r => r.CountsAgainstCapturedTotal).Sum(r => r.Amount);

    /// <summary>How much of the capture can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Adds a refund, enforcing that the order is fulfilled and that the refund never takes the total
    /// refunded beyond what was captured.
    /// </summary>
    public OrderRefund AddRefund(string idempotencyKey, decimal? amount)
    {
        if (Status != OrderPaymentStatus.Fulfilled && Status != OrderPaymentStatus.PartiallyRefunded)
            throw new PaymentValidationException(
                "The order has not been fulfilled, so its payment cannot be refunded.");

        if (CaptureId is null || CapturedAmount is null)
            throw new PaymentValidationException("There is no captured payment to refund.");

        var refundAmount = amount ?? RefundableRemaining();
        if (refundAmount <= 0m)
            throw new PaymentValidationException("There is nothing left to refund on this order.");

        if (refundAmount > RefundableRemaining())
            throw new PaymentValidationException(
                $"Refund of {refundAmount} {Currency} exceeds the refundable remaining " +
                $"{RefundableRemaining()} {Currency} of the captured payment.");

        var refund = new OrderRefund(idempotencyKey, refundAmount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    /// <summary>Re-evaluates the payment status after a refund result is recorded.</summary>
    public void RefreshRefundState()
    {
        var refunded = TotalRefunded();
        if (CapturedAmount is null) return;
        if (refunded <= 0m)
            Status = OrderPaymentStatus.Fulfilled;
        else if (refunded >= CapturedAmount.Value)
            Status = OrderPaymentStatus.Refunded;
        else
            Status = OrderPaymentStatus.PartiallyRefunded;
    }

    /// <summary>Finds an existing refund by its idempotency key (for repeated-request detection).</summary>
    public OrderRefund? FindRefund(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
