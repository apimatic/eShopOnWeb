using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal-side state of a single order's payment: the authorization hold,
/// the capture taken at fulfilment, and every refund issued against that capture.
/// One-to-one with <see cref="Order"/>, kept as its own aggregate root so payment state
/// can be updated independently of the order it belongs to.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, decimal amount, string currency, string payPalOrderId,
        string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        OrderId = orderId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RemainingRefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordAuthorizationStatus(string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordVoid(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        AuthorizationStatus = status;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string refundId, string status, decimal amount, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        // Small tolerance for decimal round-trips through string-formatted PayPal amounts.
        if (amount > RemainingRefundableAmount + 0.005m)
        {
            throw new RefundExceedsCapturedAmountException(amount, RemainingRefundableAmount);
        }

        var refund = new PaymentRefund(Id, refundId, status, amount, idempotencyKey);
        _refunds.Add(refund);
        RefundedAmount += amount;
        return refund;
    }
}
