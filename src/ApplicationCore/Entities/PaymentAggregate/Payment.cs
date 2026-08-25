using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the PayPal-side state (hold, capture, refunds) for a single Order's payment.
/// Kept as its own aggregate root, referenced by OrderId, so payment processing does not
/// have to load/mutate the Order aggregate to record PayPal state.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string currency, decimal amount)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        Currency = currency;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? AuthorizationRequestId { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }

    public decimal RefundedAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - RefundedAmount;

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string status, DateTimeOffset? expiresAt, string requestId)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new InvalidOrderStateException($"Payment for order {OrderId} cannot be authorized from state {Status}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationRequestId = requestId;
        Status = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Payment for order {OrderId} cannot be reauthorized from state {Status}.");
        }

        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Payment for order {OrderId} cannot be captured from state {Status}.");
        }

        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = feeAmount;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void RecordVoid(string status)
    {
        if (Status != PaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Payment for order {OrderId} cannot be voided from state {Status}.");
        }

        AuthorizationStatus = status;
        Status = PaymentStatus.Voided;
    }

    public Refund RecordRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey, decimal? totalRefundedAmount)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Payment for order {OrderId} cannot be refunded from state {Status}.");
        }

        var refund = new Refund(Id, payPalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);

        RefundedAmount = totalRefundedAmount ?? RefundedAmount + amount;
        Status = RefundedAmount >= CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;

        return refund;
    }
}
