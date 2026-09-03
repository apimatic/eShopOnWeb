using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money-movement state for one <see cref="OrderAggregate.Order"/>. This is an additive aggregate:
/// the existing Order aggregate is reused unchanged for catalog/items, and this holds the payment and
/// fulfilment state plus every id/status PayPal owns (the hold, the capture and the refunds) so a later
/// request can act on the payment, not only the one that started it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        CurrencyCode = currencyCode;
        Amount = amount;
        InvoiceId = invoiceId;
        Status = PaymentStatus.AwaitingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of this payment. One shopper must never act on another's payment.</summary>
    public string BuyerId { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>Order total that is authorized/held. Equals the order total to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>Correlation key stamped onto the PayPal order (invoice_id) for reconciliation.</summary>
    public string InvoiceId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // --- PayPal-owned state ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }

    /// <summary>What PayPal reported at capture: gross captured, its fee, and net proceeds to the merchant.</summary>
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Cumulative amount refunded across all refunds of this payment.</summary>
    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    /// <summary>Amount of the capture still available to refund. Never negative.</summary>
    public decimal RemainingRefundable => Math.Max(0m, (CapturedGross ?? 0m) - TotalRefunded);

    public void MarkAuthorized(string payPalOrderId, string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replace the live authorization after a reauthorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void MarkFulfilled(string captureId, decimal capturedGross, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedGross = capturedGross;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
        Touch();
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = TotalRefunded >= (CapturedGross ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
