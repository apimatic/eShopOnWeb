using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Carries the payment state that PayPal owns for an <see cref="Order"/> — enough of it
/// (ids and current status for the hold, the capture and the refunds) that a later request
/// can act on the payment, not only the one that started it.
/// Part of the Order aggregate; mutated only through the owning <see cref="Order"/>.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    public OrderPayment(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    /// <summary>ISO-4217 currency code the payment is denominated in (from configuration).</summary>
    public string Currency { get; private set; }

    /// <summary>
    /// The reference echoed to PayPal (as custom_id) for this payment and used to reconcile the
    /// transaction back to this order. Globally unique so it never collides with another transaction.
    /// </summary>
    public string? CustomReference { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    // --- Refunds ---
    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    internal void SetAuthorization(string payPalOrderId, string authorizationId, string? status,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt, string customReference)
    {
        CustomReference = customReference;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void ReplaceAuthorization(string authorizationId, string? status, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void SetCapture(string captureId, string? status, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
    }

    internal void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    internal void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>Amount still available to refund against the capture (never negative).</summary>
    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }

    public bool HasRefundWithKey(string idempotencyKey) =>
        _refunds.Any(r => r.IdempotencyKey == idempotencyKey);

    public OrderRefund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
