using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Holds the money-movement state that PayPal owns for a single eShop <c>Order</c>: the PayPal order,
/// the authorization (hold), the capture, and any refunds. It carries enough of PayPal's identifiers
/// and current statuses that a later request (fulfil, cancel, refund) can act on it, not only the
/// request that created it. A Payment belongs to exactly one Order and one buyer.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currency, string payPalOrderId, string invoiceId)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The invoice id sent to PayPal (also used to line the payment up in reconciliation).</summary>
    public string InvoiceId { get; private set; }

    /// <summary>The order total that was (or will be) authorized, in <see cref="Currency"/>.</summary>
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    // --- The hold (authorization) ---
    public string PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // A safe description of the instrument used, so an operator/shopper can recognise the payment.
    public string? CardLast4 { get; private set; }
    public string? CardBrand { get; private set; }

    // --- The capture (money actually taken at fulfilment) ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public bool IsAuthorized => !string.IsNullOrEmpty(AuthorizationId);
    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public void SetAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt, string? cardLast4, string? cardBrand)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        CardLast4 = cardLast4 ?? CardLast4;
        CardBrand = cardBrand ?? CardBrand;
    }

    /// <summary>
    /// Replaces the current hold with a freshly re-authorized one (used when the original
    /// authorization has gone stale before fulfilment).
    /// </summary>
    public void RenewAuthorization(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void SetCapture(string captureId, string status, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
    }

    public decimal TotalRefunded() =>
        _refunds.Where(r => !string.Equals(r.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                         && !string.Equals(r.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
                .Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (CapturedAmount ?? 0m) - TotalRefunded();

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Records a refund. Guards that a capture exists and that the refund never returns more than
    /// what was captured (across all refunds), so a partly-refunded order can never become
    /// refundable beyond the captured amount.
    /// </summary>
    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (!IsCaptured)
        {
            throw new InvalidOperationException("Cannot refund a payment that has not been captured.");
        }
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        if (amount > RefundableRemaining())
        {
            throw new InvalidOperationException(
                $"Refund of {amount} {Currency} exceeds the refundable remaining amount of {RefundableRemaining()} {Currency}.");
        }

        var refund = new PaymentRefund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);
        return refund;
    }

    public bool IsFullyRefunded() => IsCaptured && RefundableRemaining() <= 0m;
}
