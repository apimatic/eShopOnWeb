using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Carries the payment and fulfilment state for a single eShop <see cref="OrderAggregate.Order"/>,
/// including the PayPal-owned identifiers and statuses (order hold, capture, refunds) needed for a
/// later request to act on the payment. This is additive: the Order aggregate is unchanged.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        CurrencyCode = Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Status = OrderPaymentStatus.AwaitingPayment;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    /// <summary>The id of the reused eShop <see cref="OrderAggregate.Order"/>.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (eShop BuyerId = the caller's username/email). Used for scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Order total to be held/captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>ISO-4217 currency (from configuration).</summary>
    public string CurrencyCode { get; private set; }

    public OrderPaymentStatus Status { get; private set; }

    // --- PayPal-owned state ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    // --- Safe payment-instrument description (never full card details) ---
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    /// <summary>Operator-facing message for a state that needs action (e.g. auth cannot be renewed).</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;

    /// <summary>Records a successful authorization (hold) placed at PayPal.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? authorizationStatus,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4)
    {
        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        LastError = null;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Replaces the authorization id/expiry after a re-authorization of a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    /// <summary>Records that the held funds were captured (money taken) at fulfilment.</summary>
    public void MarkFulfilled(string captureId, string? captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        LastError = null;
        Status = OrderPaymentStatus.Fulfilled;
        Touch();
    }

    /// <summary>Records that the hold was released before fulfilment; no money moved.</summary>
    public void MarkCanceled()
    {
        AuthorizationStatus = "VOIDED";
        Status = OrderPaymentStatus.Canceled;
        LastError = null;
        Touch();
    }

    /// <summary>Adds a refund and moves the payment to partially/fully refunded.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        Status = TotalRefunded() >= (CapturedAmount ?? Amount)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        Touch();
    }

    public void MarkFailed(string error)
    {
        LastError = error;
        Status = OrderPaymentStatus.Failed;
        Touch();
    }

    /// <summary>Total of refunds already recorded against the capture.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable — never more than what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>True if a refund with this idempotency key was already processed.</summary>
    public PaymentRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
