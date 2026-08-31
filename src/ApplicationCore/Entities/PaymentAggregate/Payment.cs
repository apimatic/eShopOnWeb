using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment for an <see cref="OrderAggregate.Order"/>. Carries the PayPal-owned state
/// (order id, authorization id/status/expiry, capture id/amount/fee/net, refunds)
/// so that any later request can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Unique-per-payment correlation id. All PayPal idempotency keys derive from it, so a retry
    /// of the same payment replays safely while two different payments never share a key — even
    /// across application restarts that reset entity ids.
    /// </summary>
    public string CorrelationId { get; private set; } = Guid.NewGuid().ToString("N");

    // PayPal order + authorization (the hold)
    public string? PayPalOrderId { get; private set; }

    /// <summary>
    /// Caller-provided invoice number sent to PayPal. Unique per payment (PayPal business
    /// accounts can reject reused invoice ids), generated once when the PayPal order is created.
    /// </summary>
    public string? InvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int AuthorizationRenewals { get; private set; }

    // PayPal capture (the money taken at fulfilment)
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount =>
        CapturedAmount.HasValue ? decimal.Round(CapturedAmount.Value - TotalRefunded, 2) : 0m;

    public void SetPayPalOrderId(string payPalOrderId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
    }

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.AwaitingPayment)
        {
            throw new PaymentDomainException($"Payment for order {OrderId} cannot be authorized while in status {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkAuthorizationRenewed(string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentDomainException($"Payment for order {OrderId} cannot renew an authorization while in status {Status}.");
        }

        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationRenewals++;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new PaymentDomainException($"Payment for order {OrderId} cannot be captured while in status {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = payPalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        if (Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentDomainException($"Payment for order {OrderId} has already been captured; issue a refund instead of cancelling.");
        }
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status, string? noteToPayer)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentDomainException($"Payment for order {OrderId} cannot be refunded while in status {Status}.");
        }
        if (_refunds.Any(r => r.IdempotencyKey == idempotencyKey))
        {
            throw new PaymentDomainException($"A refund with idempotency key '{idempotencyKey}' already exists for order {OrderId}.");
        }
        if (amount > RefundableAmount)
        {
            throw new PaymentDomainException(
                $"Refund of {amount:0.00} {Currency} exceeds the remaining refundable amount of {RefundableAmount:0.00} {Currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, status, noteToPayer);
        _refunds.Add(refund);

        Status = RefundableAmount == 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
