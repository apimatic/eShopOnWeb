using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Records the PayPal payment state for an order: the authorization (hold),
/// the capture (settlement at fulfilment) and any refunds.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal orderTotal, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(orderTotal, nameof(orderTotal));

        OrderId = orderId;
        BuyerId = buyerId;
        OrderTotal = orderTotal;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public decimal OrderTotal { get; private set; }
    public string Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }

    /// <summary>
    /// Invoice id sent to PayPal; unique per payment so it survives shared
    /// sandbox accounts and database reseeding. Used for reconciliation.
    /// </summary>
    public string? InvoiceId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status is not ("FAILED" or "CANCELLED"))
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetAuthorization(string payPalOrderId, string invoiceId, string authorizationId, string status, decimal amount, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void UpdateAuthorization(string authorizationId, string status, decimal amount, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        Touch();
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Touch();
    }

    public PaymentRefund AddRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status, string? note)
    {
        var refund = new PaymentRefund(payPalRefundId, idempotencyKey, amount, status, note);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
