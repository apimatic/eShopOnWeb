using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Tracks the PayPal state for one order: the authorization (hold), the capture
/// (money taken at fulfilment) and any refunds against the capture. Stored as part
/// of the <see cref="Order"/> aggregate so later requests can act on the ids PayPal owns.
/// </summary>
public class Payment : BaseEntity
{
    public int OrderId { get; private set; }

    // PayPal order created to hold the funds (intent=AUTHORIZE).
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    // Raw PayPal authorization status, e.g. CREATED, CAPTURED, VOIDED, PENDING.
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpirationTime { get; private set; }
    public decimal AuthorizedAmount { get; private set; }

    // Capture (relief of the hold at fulfilment time).
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? FeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }

    public string Currency { get; private set; } = "USD";
    public PaymentStatus Status { get; private set; } = PaymentStatus.Authorized;

    // Saved card (PayPal vault id) used for the payment, when paid from a saved card.
    public string? CardVaultId { get; private set; }

    /// <summary>
    /// The unique invoice id sent to PayPal for this authorization. Stored so the
    /// reconciliation report can match PayPal transactions back to this order exactly.
    /// </summary>
    public string? InvoiceId { get; private set; }

    /// <summary>1 + number of times the hold has been renewed; seeds idempotent PayPal request ids.</summary>
    public int AuthorizationGeneration { get; private set; } = 1;

    private readonly List<Refund> _refunds = new List<Refund>();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Where(r => r.IsEffective).Sum(r => r.Amount);
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - TotalRefunded;

#pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }
#pragma warning restore CS8618

    public Payment(decimal authorizedAmount, string currency, string? cardVaultId = null)
    {
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        CardVaultId = cardVaultId;
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records the PayPal ids returned when the authorization was created.</summary>
    public void RecordAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime, string? invoiceId = null)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
        if (!string.IsNullOrEmpty(invoiceId))
        {
            InvoiceId = invoiceId;
        }
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Refreshes the authorization status from PayPal (used when renewing a stale hold).</summary>
    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expirationTime)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpirationTime = expirationTime;
    }

    /// <summary>Bumps the generation counter when the hold has been renewed.</summary>
    public int IncrementAuthorizationGeneration()
    {
        AuthorizationGeneration++;
        return AuthorizationGeneration;
    }

    public bool IsAuthorizationUsable =>
        !string.IsNullOrEmpty(AuthorizationId) &&
        AuthorizationStatus is not "VOIDED" and not "DENIED" &&
        !(AuthorizationExpirationTime.HasValue && AuthorizationExpirationTime.Value <= DateTimeOffset.UtcNow);

    /// <summary>Records the capture performed at fulfilment, including the amounts PayPal reported.</summary>
    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? feeAmount, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        Status = PaymentStatus.Captured;
    }

    /// <summary>Marks the authorization as released (cancel/void or expiry).</summary>
    public void MarkAuthorizationReleased(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.AuthorizationReleased;
    }

    /// <summary>Adds a refund PayPal reported for the capture.</summary>
    public Refund AddRefund(string refundId, decimal amount, string refundStatus, DateTimeOffset? completedTime, string? idempotencyKey = null)
    {
        var refund = new Refund(refundId, amount, refundStatus, completedTime, idempotencyKey);
        _refunds.Add(refund);
        RecalculateRefundStatus();
        return refund;
    }

    /// <summary>Updates an existing refund (e.g. pending -> completed) after a PayPal call.</summary>
    public void UpdateRefund(Refund refund, string refundStatus, DateTimeOffset? completedTime)
    {
        refund.UpdateStatus(refundStatus, completedTime);
        RecalculateRefundStatus();
    }

    public Refund? FindRefundByPayPalId(string refundId) => _refunds.FirstOrDefault(r => r.RefundId == refundId);

    /// <summary>Returns the refund created under the given caller idempotency key, if any.</summary>
    public Refund? FindRefundByKey(string idempotencyKey) => _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    private void RecalculateRefundStatus()
    {
        if (Status != PaymentStatus.Captured && Status != PaymentStatus.PartiallyRefunded && Status != PaymentStatus.Refunded)
            return;

        if (TotalRefunded >= (CapturedAmount ?? 0m))
            Status = PaymentStatus.Refunded;
        else if (TotalRefunded > 0m)
            Status = PaymentStatus.PartiallyRefunded;
        else
            Status = PaymentStatus.Captured;
    }
}
