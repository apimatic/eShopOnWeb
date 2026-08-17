using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Tracks the payment and fulfilment state for a single <see cref="OrderAggregate.Order"/>. This is an
/// additive aggregate: the existing Order/OrderItem model is untouched, and this type owns everything the
/// money movement needs — the amount to collect and the PayPal-owned ids and statuses for the hold
/// (authorization), the capture, and any refunds — so a later request can act on the payment, not only
/// the request that started it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        Status = PaymentStatus.AwaitingPayment;
        // Stable, unique reference used as the PayPal invoice_id. It correlates eShop payments to PayPal
        // transactions during reconciliation and seeds the idempotency keys for authorize/capture.
        PaymentReference = $"ESHOP-{Guid.NewGuid():N}";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The order total to collect, to the cent. The amount PayPal holds must equal this.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }
    public string PaymentReference { get; private set; }

    // --- Hold (authorization) state owned by PayPal ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>A safe, human-recognisable description of the instrument used, e.g. "VISA ****1111". Never full card data.</summary>
    public string? PaymentSourceDescription { get; private set; }

    // --- Capture state owned by PayPal (populated at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CanceledAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>The PayPal custom_id set on the order/capture, used to line transactions up with this order.</summary>
    public static string BuildCustomId(int orderId) => $"ESHOP-ORDER-{orderId}";

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? paymentSourceDescription)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentSourceDescription = paymentSourceDescription;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Replaces a stale hold with a freshly renewed one before capture.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
    }

    public void MarkCanceled()
    {
        AuthorizationStatus = "VOIDED";
        CanceledAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Canceled;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never lets refunds exceed the capture.</summary>
    public decimal RefundableRemaining() => Math.Round((CapturedAmount ?? 0m) - TotalRefunded(), 2, MidpointRounding.AwayFromZero);

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = RefundableRemaining() <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}
