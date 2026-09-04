using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currency, decimal amountAuthorized)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amountAuthorized, nameof(amountAuthorized));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AmountAuthorized = amountAuthorized;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public decimal AmountAuthorized { get; private set; }

    // PayPal state: ids and statuses PayPal owns, needed for later actions
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }

    public int? SavedPaymentMethodId { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; } = DateTimeOffset.Now;
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    public void RecordAuthorization(string payPalOrderId, string authorizationId,
        string authorizationStatus, DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        AuthorizedAt = DateTimeOffset.Now;
    }

    public void RecordCapture(string captureId, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.Now;
        AuthorizationStatus = "CAPTURED";
    }

    public void UpdateAuthorizationStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationStatus = status;
    }

    public bool IsRefundKeyUsed(string idempotencyKey)
    {
        return _refunds.Any(r => r.IdempotencyKey == idempotencyKey);
    }

    public PaymentRefund AddRefund(string idempotencyKey, decimal amount, string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (CapturedAmount <= 0)
        {
            throw new PaymentDomainException("Cannot refund a payment that has not been captured.");
        }
        if (RefundedAmount + amount > CapturedAmount)
        {
            throw new PaymentDomainException(
                $"Refund of {amount:0.00} {Currency} exceeds the refundable amount. " +
                $"Captured {CapturedAmount:0.00} {Currency}, already refunded {RefundedAmount:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(Id, idempotencyKey, amount, payPalRefundId, status);
        _refunds.Add(refund);
        return refund;
    }
}
