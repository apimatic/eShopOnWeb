using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// Payment state owned by PayPal (ids and current status for the hold, the capture and the
/// refunds) so that any later request can act on it, not only the one that started it.
/// Full card details are never stored here.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() {}

    public Payment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        OrderId = orderId;
        BuyerId = buyerId;
        AuthorizedAmount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Currency { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PaidWithVaultTokenId { get; private set; }

    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public bool IsVoided { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Sum(r => r.Amount);

    public bool IsAuthorized =>
        !string.IsNullOrEmpty(AuthorizationId) && !IsVoided && string.IsNullOrEmpty(CaptureId);

    public bool IsCaptured => !string.IsNullOrEmpty(CaptureId);

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string status,
        DateTimeOffset? expiresAt, string? vaultTokenId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        PaidWithVaultTokenId = vaultTokenId;
        IsVoided = false;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
    }

    public void MarkCaptured(string captureId, string status, decimal capturedAmount,
        decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void MarkVoided(string status)
    {
        IsVoided = true;
        AuthorizationStatus = status;
    }

    public PaymentRefund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new PaymentRefund(refundId, amount, Currency, status, idempotencyKey);
        _refunds.Add(refund);
        if (CaptureStatus == "COMPLETED")
        {
            CaptureStatus = TotalRefunded >= (CapturedAmount ?? 0m) ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }
        return refund;
    }

    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - TotalRefunded;
}
