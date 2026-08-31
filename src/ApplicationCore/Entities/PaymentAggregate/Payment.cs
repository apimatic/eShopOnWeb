using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal-owned state (order/authorization/capture/refund ids and statuses)
/// for one eShop order, so any later request can act on the payment.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
        CreatedOn = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }

    /// <summary>
    /// Globally unique invoice id sent to PayPal (the merchant account requires uniqueness);
    /// also the deterministic reconciliation join key. Format: order-{orderId}-{paymentId}-{suffix}.
    /// </summary>
    public string? InvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? SellerFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedOn { get; private set; }
    public DateTimeOffset? CapturedOn { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.IsEffective)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void RecordPayPalOrder(string payPalOrderId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
    }

    public void RecordAuthorization(string authorizationId, string? status, DateTimeOffset? expirationTime)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpirationTime = expirationTime;
        Status = PaymentStatus.Authorized;
    }

    public void UpdateAuthorizationStatus(string? status, DateTimeOffset? expirationTime)
    {
        AuthorizationStatus = status;
        AuthorizationExpirationTime = expirationTime;
    }

    public void RecordCapture(string captureId, string? status, decimal? grossAmount, decimal? sellerFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        AuthorizationStatus = "CAPTURED";
        CapturedAmount = grossAmount;
        SellerFee = sellerFee;
        NetAmount = netAmount;
        CapturedOn = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? AuthorizationStatus;
        Status = PaymentStatus.Voided;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    /// <summary>
    /// Returns the payment to a state in which the shopper can pay again,
    /// after an authorization could not be renewed.
    /// </summary>
    public void RequireRepayment()
    {
        AuthorizationId = null;
        AuthorizationStatus = null;
        AuthorizationExpirationTime = null;
        Status = PaymentStatus.Pending;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string? payPalRefundId, decimal amount, string? status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var refund = new PaymentRefund(Id, idempotencyKey, payPalRefundId, amount, Currency, status);
        _refunds.Add(refund);

        if (Status == PaymentStatus.Captured || Status == PaymentStatus.PartiallyRefunded)
        {
            Status = TotalRefunded >= (CapturedAmount ?? 0m)
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        }

        return refund;
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }
}
