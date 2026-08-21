using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Holds the payment/fulfilment state of an <see cref="Order"/>, additive to the existing order model.
/// One <see cref="OrderPayment"/> exists per order, keyed by <see cref="OrderId"/>. It carries enough of the
/// state PayPal owns — the hold, the capture and the refunds, each with its id and current status — that a
/// later request can act on the payment, not only the one that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order this payment belongs to. Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The order total this payment is for, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- State that PayPal owns ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Idempotency keys, stable per logical operation and persisted so a retry de-duplicates ---
    public string? AuthorizationRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of every refund already recorded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount of the capture still available to refund.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>
    /// Returns the stable authorization idempotency key, generating and storing one on first use so the same
    /// key is reused across retries of the authorize request.
    /// </summary>
    public string EnsureAuthorizationRequestId(Func<string> keyFactory)
    {
        if (string.IsNullOrEmpty(AuthorizationRequestId))
        {
            AuthorizationRequestId = keyFactory();
        }
        return AuthorizationRequestId!;
    }

    /// <summary>Returns the stable capture idempotency key, generating and storing one on first use.</summary>
    public string EnsureCaptureRequestId(Func<string> keyFactory)
    {
        if (string.IsNullOrEmpty(CaptureRequestId))
        {
            CaptureRequestId = keyFactory();
        }
        return CaptureRequestId!;
    }

    public void SetPayPalOrderId(string payPalOrderId) => PayPalOrderId = payPalOrderId;

    public void MarkAuthorized(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records that the authorization was renewed (re-authorized) before fulfilment.</summary>
    public void UpdateAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkRequiresApproval()
    {
        Status = PaymentStatus.RequiresApproval;
    }

    public void MarkFailed()
    {
        Status = PaymentStatus.Failed;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        if (AuthorizationStatus is null || !AuthorizationStatus.Equals("CAPTURED", StringComparison.OrdinalIgnoreCase))
        {
            AuthorizationStatus = "CAPTURED";
        }
        Status = PaymentStatus.Captured;
    }

    public void MarkCancelled(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus ?? "VOIDED";
        Status = PaymentStatus.Cancelled;
    }

    /// <summary>
    /// Records a refund and advances the status to partially- or fully-refunded. Rejects a refund that would
    /// take total refunds beyond the captured amount, so a partly-refunded order never becomes refundable
    /// beyond what was captured.
    /// </summary>
    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);

        if (TotalRefunded() >= (CapturedAmount ?? 0m))
        {
            Status = PaymentStatus.Refunded;
        }
        else
        {
            Status = PaymentStatus.PartiallyRefunded;
        }
    }
}
