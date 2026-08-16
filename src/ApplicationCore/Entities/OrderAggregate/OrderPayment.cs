using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that the payment processor (PayPal) owns for a single
/// <see cref="Order"/>: the ids and current status of the authorization (hold), the
/// capture, and any refunds. It holds enough state that a later request (fulfil, cancel,
/// refund, reconcile) can act on the payment, not only the request that started it.
///
/// This is a 1:1 companion to <see cref="Order"/> keyed by <see cref="OrderId"/>; it is a
/// separate aggregate so it can be loaded and updated independently of the order graph.
/// No card number is ever stored here — only processor tokens and safe display metadata.
/// </summary>
public class OrderPayment : BaseEntity, IAggregateRoot
{
    public const string PayPalProvider = "PayPal";

    private readonly List<PaymentRefund> _refunds = new();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, decimal amount, string currencyCode, string payPalOrderId,
        string invoiceId, string requestId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));

        OrderId = orderId;
        Amount = amount;
        CurrencyCode = currencyCode;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        RequestId = requestId;
    }

    public int OrderId { get; private set; }
    public string Provider { get; private set; } = PayPalProvider;

    /// <summary>The authorized order total (equal to the amount PayPal holds, to the cent).</summary>
    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The PayPal Orders-v2 order id created when authorizing.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>A merchant reference correlated back into PayPal transaction reporting.</summary>
    public string InvoiceId { get; private set; }

    /// <summary>Idempotency key (PayPal-Request-Id) used for the authorize call.</summary>
    public string RequestId { get; private set; }

    // Safe, non-sensitive display metadata about the funding instrument.
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }

    // ----- Authorization (the hold) -----
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }

    // ----- Capture (the money actually taken) -----
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Total value of refunds that PayPal has completed against the capture.</summary>
    public decimal TotalRefunded =>
        _refunds.Where(r => r.IsEffective).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetCardMetadata(string? brand, string? last4)
    {
        CardBrand = brand;
        CardLast4 = last4;
    }

    public void SetAuthorization(string authorizationId, string authorizationStatus)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
    }

    public void SetCapture(string captureId, string captureStatus, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        // The hold has been consumed by the capture.
        AuthorizationStatus = "CAPTURED";
    }

    public void SetVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public PaymentRefund AddRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }
}
