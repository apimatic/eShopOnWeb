using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// The payment for an order. Carries the identifiers and current status that the payment
/// provider (PayPal) owns — the order (hold), the authorization, the capture and any
/// refunds — so that any later request can act on the payment, not only the one that
/// started it. Never stores full card details.
/// </summary>
public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() {}

    public OrderPayment(int orderId, string payPalOrderId, string invoiceId, string attemptId, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(invoiceId, nameof(invoiceId));
        Guard.Against.NullOrEmpty(attemptId, nameof(attemptId));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        OrderId = orderId;
        PayPalOrderId = payPalOrderId;
        InvoiceId = invoiceId;
        AttemptId = attemptId;
        AuthorizedAmount = amount;
        CurrencyCode = currencyCode;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>
    /// Unique id for this payment attempt. All provider idempotency keys are derived from
    /// it, so keys stay unique even when order ids repeat (e.g. shared sandbox accounts).
    /// </summary>
    public string AttemptId { get; private set; }

    /// <summary>PayPal's id for the checkout order resource.</summary>
    public string PayPalOrderId { get; private set; }

    /// <summary>The unique invoice id sent to PayPal for this payment (used in reconciliation).</summary>
    public string InvoiceId { get; private set; }

    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiry { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string CurrencyCode { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    /// <summary>How many times the authorization has been renewed.</summary>
    public int ReauthorizationCount { get; private set; }

    // Safe, non-sensitive description of the instrument used (never a full PAN or CVC).
    public string? CardBrand { get; private set; }
    public string? CardLastDigits { get; private set; }
    public string? VaultTokenId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds
        .Where(r => r.Status != PaymentRefundStatus.Failed && r.Status != PaymentRefundStatus.Cancelled)
        .Sum(r => r.Amount);

    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

    public void MarkAuthorized(string authorizationId, string status, decimal amount, DateTimeOffset? expiry,
        string? cardBrand, string? cardLastDigits, string? vaultTokenId)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiry = expiry;
        CardBrand = cardBrand;
        CardLastDigits = cardLastDigits;
        VaultTokenId = vaultTokenId;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkReauthorized(string authorizationId, string status, decimal amount, DateTimeOffset? expiry)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiry = expiry;
        ReauthorizationCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAuthorizationVoided(string status)
    {
        AuthorizationStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCaptured(string captureId, string status, decimal capturedAmount, decimal? fee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = netAmount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public PaymentRefund AddRefund(string refundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var refund = new PaymentRefund(Id, refundId, idempotencyKey, amount, CurrencyCode, status);
        _refunds.Add(refund);
        UpdatedAt = DateTimeOffset.UtcNow;
        return refund;
    }
}
