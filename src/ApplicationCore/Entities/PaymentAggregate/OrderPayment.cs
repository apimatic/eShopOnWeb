using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the payment and fulfilment state that follows an <see cref="OrderAggregate.Order"/>.
/// This is an additive aggregate (1:1 with an order) that holds the state PayPal owns — the
/// ids and current status of the hold (authorization), the capture and the refunds — so a
/// later request can act on it. It never stores card numbers or CVVs.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = OrderPaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
        IdempotencyToken = Guid.NewGuid();
        // Unique per payment so it never collides with a prior run at PayPal (in-memory store
        // reuses order ids), while still tying the PayPal transaction back to this eShop order.
        InvoiceId = $"ESHOP-{orderId}-{IdempotencyToken:N}";
    }

    /// <summary>
    /// Stable, per-payment token used to build PayPal-Request-Id idempotency keys. Retries of the
    /// same logical operation reuse it (so a double-click never charges twice), while a fresh
    /// payment gets a fresh token (so keys never collide across runs or a reset in-memory store).
    /// </summary>
    public Guid IdempotencyToken { get; private set; }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment. One shopper never sees or acts on another's.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total, to the cent. The amount authorized at PayPal equals this.</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>Stable, order-scoped invoice id sent to PayPal so reconciliation can line the two up.</summary>
    public string InvoiceId { get; private set; }

    public OrderPaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>Safe description of the instrument used to pay, e.g. "VISA ****1111". Never full card details.</summary>
    public string? PaymentInstrumentDescription { get; private set; }

    /// <summary>Set when a saved card was used, so it is visible which instrument paid.</summary>
    public int? SavedPaymentMethodId { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => AuthorizationId is not null;
    public bool IsCaptured => CaptureId is not null;

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, string? instrumentDescription, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentInstrumentDescription = instrumentDescription;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = OrderPaymentStatus.Authorized;
    }

    /// <summary>Replaces the hold with a renewed authorization (after reauthorize).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? authorizationExpiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        AuthorizationStatus = "VOIDED";
        Status = OrderPaymentStatus.Cancelled;
    }

    public decimal TotalRefunded() => _refunds
        .Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(r => r.Amount);

    /// <summary>How much of the capture is still refundable. Never negative.</summary>
    public decimal RefundableAmount()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining < 0m ? 0m : remaining;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);

        if (RefundableAmount() <= 0m)
        {
            Status = OrderPaymentStatus.Refunded;
        }
        else if (TotalRefunded() > 0m)
        {
            Status = OrderPaymentStatus.PartiallyRefunded;
        }
    }
}
