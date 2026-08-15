using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// The money side of an order. One payment per order. Holds enough of the state PayPal owns — the
/// hold (authorization), the capture, and each refund, each with its id and current status — that a
/// later request can act on it, not only the request that created it.
/// </summary>
public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Payment() { }

    public Payment(int orderId, string buyerId, decimal amount, string currencyCode)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = PaymentStatus.Pending;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order/payment; enforces per-shopper scoping.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Amount to authorize/capture — equals the order total, to the cent.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    public PaymentStatus Status { get; private set; }

    /// <summary>Safe descriptor of the instrument used (e.g. "Visa ****1111"). Never full card details.</summary>
    public string? InstrumentDescription { get; private set; }

    // --- Hold (authorization) ---
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    // --- Capture ---
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<Refund> _refunds = new();
    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded => _refunds.Where(r => r.CountsAgainstBalance).Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining => (CapturedAmount ?? 0m) - TotalRefunded;

    public void SetAuthorization(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? expiresAt, string? instrumentDescription)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        if (instrumentDescription is not null)
        {
            InstrumentDescription = instrumentDescription;
        }
        Status = PaymentStatus.Authorized;
    }

    /// <summary>Records a renewed authorization id/expiry after a re-authorization.</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Status = PaymentStatus.Authorized;
    }

    public void MarkVoided()
    {
        AuthorizationStatus = "VOIDED";
        Status = PaymentStatus.Voided;
    }

    public void SetCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public Refund? FindRefundByKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    /// <summary>
    /// Guards that a new refund cannot take the total refunded beyond what was captured, then records it.
    /// </summary>
    public Refund AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (amount <= 0m)
        {
            throw PaymentApiException.BadRequest("Refund amount must be greater than zero.");
        }
        if (amount > RefundableRemaining)
        {
            throw PaymentApiException.Conflict(
                $"Refund of {amount:0.00} exceeds the refundable balance of {RefundableRemaining:0.00} {CurrencyCode}.");
        }

        var refund = new Refund(refundId, amount, status, idempotencyKey);
        _refunds.Add(refund);

        Status = RefundableRemaining <= 0m ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
