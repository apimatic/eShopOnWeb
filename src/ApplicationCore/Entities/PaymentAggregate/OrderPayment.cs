using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money/fulfilment state that follows a real payment for an existing <c>Order</c>. Linked to the
/// order by <see cref="OrderId"/> (one payment per order). Carries the PayPal-owned identifiers and
/// statuses (order, authorization, capture, refunds) so any later request can act on it.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, string currencyCode, decimal amount, string invoiceId)
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
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>
    /// The external invoice id sent to PayPal — unique per merchant account across app runs, and the key
    /// both sides of reconciliation line up on.
    /// </summary>
    public string InvoiceId { get; private set; }

    /// <summary>The order total captured at placement time; the amount authorized/captured, to the cent.</summary>
    public decimal Amount { get; private set; }

    public PaymentStatus Status { get; private set; }

    // PayPal-owned state.
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal TotalRefunded { get; private set; }

    /// <summary>When PayPal actually took the money — the money-movement clock used for reconciliation.</summary>
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>Safe, PCI-free description of the instrument used (e.g. "VISA ****1111").</summary>
    public string? PaymentMethodDescription { get; private set; }

    /// <summary>Last operator-actionable failure reason, when a payment operation could not proceed.</summary>
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<RefundRecord> _refunds = new();
    public IReadOnlyCollection<RefundRecord> Refunds => _refunds.AsReadOnly();

    public void RecordPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        Touch();
    }

    public void RecordAuthorization(string authorizationId, string authorizationStatus, string? methodDescription)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        PaymentMethodDescription = methodDescription;
        Status = PaymentStatus.Authorized;
        LastError = null;
        Touch();
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        Touch();
    }

    public void RecordCapture(string captureId, string captureStatus, decimal grossAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        Status = PaymentStatus.Fulfilled;
        LastError = null;
        Touch();
    }

    public void RecordVoided()
    {
        Status = PaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
        LastError = null;
        Touch();
    }

    public void AddRefund(RefundRecord refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
        TotalRefunded += refund.Amount;
        Status = TotalRefunded >= (CapturedAmount ?? Amount)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        Touch();
    }

    public void RecordFailure(string reason)
    {
        LastError = reason;
        Touch();
    }

    /// <summary>Amount still refundable — never lets total refunds exceed what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded;

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
