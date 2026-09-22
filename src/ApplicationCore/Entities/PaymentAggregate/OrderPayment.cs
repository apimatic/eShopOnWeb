using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The PayPal payment attached to a single eShop <see cref="OrderAggregate.Order"/>. Keyed 1:1 by
/// <see cref="OrderId"/> (the primary key), so a second concurrent authorize attempt for the same
/// order is rejected by the store's primary-key uniqueness — the duplicate-charge guard. Carries
/// enough PayPal state (order id, authorization id, capture id, amounts) that a later fulfil, cancel
/// or refund request can act on it, not just the request that created it.
/// </summary>
public class OrderPayment : IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currency, decimal authorizedAmount,
        string invoiceId, Guid? savedPaymentMethodId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(authorizedAmount, nameof(authorizedAmount));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));

        OrderId = orderId;
        BuyerId = buyerId;
        Currency = currency;
        AuthorizedAmount = authorizedAmount;
        InvoiceId = invoiceId;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = PaymentStatus.Authorizing;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Primary key — equals <see cref="OrderAggregate.Order.Id"/>. The duplicate-authorize claim.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment (the shopper's user name from the JWT).</summary>
    public string BuyerId { get; private set; }

    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string InvoiceId { get; private set; }
    public Guid? SavedPaymentMethodId { get; private set; }

    public PaymentStatus Status { get; private set; }

    // PayPal-owned state — required so later requests can act on the payment.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }

    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Records a successful PayPal authorization (the funds hold).</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        LastError = null;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Records the PayPal order id even if authorization did not complete (unknown-outcome recovery aid).</summary>
    public void RecordPayPalOrderId(string payPalOrderId)
    {
        if (!string.IsNullOrEmpty(payPalOrderId))
        {
            PayPalOrderId = payPalOrderId;
            Touch();
        }
    }

    public void MarkFailed(string? error)
    {
        Status = PaymentStatus.Failed;
        LastError = error;
        Touch();
    }

    /// <summary>Replaces the authorization id/expiry after a re-authorization renewed a stale hold.</summary>
    public void RenewAuthorization(string authorizationId, string? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
        Touch();
    }

    /// <summary>Records the capture at fulfilment — this is when money actually moves.</summary>
    public void MarkFulfilled(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Fulfilled;
        LastError = null;
        Touch();
    }

    public void MarkCancelled()
    {
        Status = PaymentStatus.Cancelled;
        Touch();
    }

    /// <summary>The amount still eligible to be refunded (never below zero, never over captured).</summary>
    public decimal RemainingRefundable() => (CapturedAmount ?? 0m) - RefundedAmount;

    public bool CanRefund(decimal amount) =>
        Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded
        && amount > 0m
        && amount <= RemainingRefundable();

    /// <summary>Applies a refund against the captured amount; flips to fully-refunded when the balance is exhausted.</summary>
    public void RegisterRefund(decimal amount)
    {
        if (!CanRefund(amount))
        {
            throw new InvalidOperationException(
                $"Refund of {amount} rejected: only {RemainingRefundable()} of {CapturedAmount} remains refundable for order {OrderId}.");
        }
        RefundedAmount += amount;
        Status = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }
}
