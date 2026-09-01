using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal-owned payment state for one order: the hold (authorization), the capture,
/// and any refunds. Carries the provider ids and statuses a later request needs to act on
/// the payment, not only the request that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The order total the shopper authorized, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; } = PaymentStatus.Authorized;

    // PayPal order + authorization (the hold)
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture (the money actually taken at fulfilment), as reported by PayPal
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Adopts a renewed authorization (PayPal may issue a new authorization id).</summary>
    public void MarkAuthorizationRenewed(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be reauthorized from state {Status}.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkRequiresNewAuthorization()
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot require a new authorization from state {Status}.");
        }
        Status = PaymentStatus.RequiresNewAuthorization;
    }

    public void MarkCaptured(string captureId, decimal grossAmount, decimal? fee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != PaymentStatus.Authorized)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be captured from state {Status}.");
        }

        CaptureId = captureId;
        CapturedAmount = grossAmount;
        PayPalFee = fee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
    }

    public void MarkVoided()
    {
        if (Status != PaymentStatus.Authorized && Status != PaymentStatus.RequiresNewAuthorization)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be voided from state {Status}.");
        }
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string? status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new OrderStateException($"Payment for order {OrderId} cannot be refunded from state {Status}.");
        }
        if (_refunds.Any(r => r.IdempotencyKey == idempotencyKey))
        {
            throw new OrderStateException($"A refund with idempotency key '{idempotencyKey}' already exists for order {OrderId}.");
        }
        if (amount > RefundableRemaining)
        {
            throw new OrderStateException(
                $"Refund of {amount} {Currency} exceeds the remaining refundable amount of {RefundableRemaining} {Currency} for order {OrderId}.");
        }

        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        Status = RefundableRemaining == 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
