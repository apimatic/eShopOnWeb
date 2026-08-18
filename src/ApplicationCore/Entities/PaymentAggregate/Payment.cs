using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The payment for an <c>Order</c>. Holds enough of the state PayPal owns — the hold (authorization),
/// the capture, and the refunds, each with their PayPal id and current status — that a later request can
/// act on it, not only the one that started it. One <see cref="Payment"/> per order.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Negative(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>Order total to the cent — the amount PayPal holds must equal this.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }
    public PaymentStatus Status { get; private set; }

    // State PayPal owns for the hold.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // State PayPal reports at capture (fulfilment).
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of refunds that are not failed/cancelled — i.e. money PayPal has agreed to return.</summary>
    public decimal TotalRefunded => _refunds
        .Where(r => !IsDeadRefundStatus(r.Status))
        .Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt)
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

    /// <summary>
    /// Replace a stale authorization with a freshly-renewed one (during fulfilment). The payment stays
    /// Authorized; only the PayPal authorization id/status/expiry change.
    /// </summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void SetCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Captured;
        Touch();
    }

    public void SetCancelled()
    {
        Status = PaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
        Touch();
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
        Touch();
    }

    public Refund? FindRefundByIdempotencyKey(string idempotencyKey)
        => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Record a refund that PayPal has accepted and move the payment to Partially/fully Refunded.
    /// Guards that the total never exceeds the captured amount (PayPal enforces this too).
    /// </summary>
    public Refund RecordRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string status)
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded)
        {
            throw new InvalidPaymentOperationException(
                "Only a captured (fulfilled) payment can be refunded.");
        }

        if (amount > RemainingRefundable)
        {
            throw new PaymentValidationException(
                $"Refund of {amount} exceeds the remaining refundable amount {RemainingRefundable}.");
        }

        var refund = new Refund(idempotencyKey, amount, payPalRefundId, status);
        _refunds.Add(refund);

        Status = TotalRefunded >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
        return refund;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    private static bool IsDeadRefundStatus(string status)
        => string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
