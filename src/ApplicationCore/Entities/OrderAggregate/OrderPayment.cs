using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the state PayPal owns for an order's payment: the ids and current status
/// of the hold (authorization), the capture, and the refunds — enough that a later
/// request can act on the payment, not only the one that started it. Part of the
/// Order aggregate; mutated only through the owning <see cref="Order"/>.
/// <para>
/// No full card details are ever stored here — only PayPal-issued identifiers.
/// </para>
/// </summary>
public class OrderPayment : BaseEntity
{
    public string PayPalOrderId { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>The order total that was authorized, to the cent.</summary>
    public decimal Amount { get; private set; }

    /// <summary>The saved card used to pay, when the shopper paid with a vaulted card; otherwise null.</summary>
    public int? PaymentMethodId { get; private set; }

    // Hold (authorization)
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // Capture
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedGross { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string payPalOrderId, string currencyCode, decimal amount, int? paymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalOrderId = payPalOrderId;
        CurrencyCode = currencyCode;
        Amount = amount;
        PaymentMethodId = paymentMethodId;
    }

    public void SetAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(string captureId, string status, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedGross = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still refundable — never lets refunds exceed what was captured.</summary>
    public decimal RefundableRemaining() => (CapturedGross ?? 0m) - TotalRefunded();

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);
    }
}
