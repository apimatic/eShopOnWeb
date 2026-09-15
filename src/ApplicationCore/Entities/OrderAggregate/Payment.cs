using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Payment : BaseEntity
{
#pragma warning disable CS8618
    private Payment() { }
#pragma warning restore CS8618

    internal Payment(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
        MerchantReference = $"eshop-{Guid.NewGuid():N}";
        Status = PaymentStatus.AwaitingPayment;
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string MerchantReference { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public decimal RefundedAmount { get; private set; }
    public DateTimeOffset? VoidedAt { get; private set; }
    public string ConcurrencyStamp { get; private set; } = Guid.NewGuid().ToString("N");

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundableAmount => Math.Max(0m, (CapturedAmount ?? 0m) - _refunds
        .Where(x => x.Status is not ("FAILED" or "CANCELLED"))
        .Sum(x => x.Amount));

    public void SetPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
        Touch();
    }

    public void Authorize(string id, string authorizationStatus, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt, string payPalOrderStatus,
        bool isReauthorization = false)
    {
        if (amount != Amount)
        {
            throw new InvalidOperationException("PayPal authorized an amount different from the order total.");
        }

        AuthorizationId = id;
        AuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        PayPalOrderStatus = payPalOrderStatus;
        Status = authorizationStatus == "CREATED"
            ? PaymentStatus.Authorized
            : authorizationStatus == "PENDING"
                ? PaymentStatus.AwaitingPayment
                : PaymentStatus.Failed;
        if (isReauthorization)
        {
            ReauthorizationCount++;
        }
        Touch();
    }

    public void RecordCapture(string id, string captureStatus, decimal amount,
        decimal? fee, decimal? net, DateTimeOffset? capturedAt)
    {
        if (amount != Amount)
        {
            throw new InvalidOperationException("PayPal captured an amount different from the order total.");
        }

        CaptureId = id;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
        Status = string.Equals(captureStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? PaymentStatus.Captured
            : PaymentStatus.CapturePending;
        Touch();
    }

    public void MarkVoided(string authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        Status = PaymentStatus.Voided;
        VoidedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public PaymentRefund StartRefund(string idempotencyKey, decimal amount)
    {
        if (Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }
        if (amount <= 0m || amount > RefundableAmount)
        {
            throw new InvalidOperationException($"Refund amount must be positive and no more than {RefundableAmount:0.00} {Currency}.");
        }

        var refund = new PaymentRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public PaymentRefund? FindRefund(string idempotencyKey) =>
        _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);

    public void CompleteRefund(PaymentRefund refund, string payPalRefundId, string refundStatus,
        decimal amount, DateTimeOffset? createdAt)
    {
        refund.Complete(payPalRefundId, refundStatus, amount, createdAt);
        RefundedAmount = _refunds
            .Where(x => x.Status == "COMPLETED")
            .Sum(x => x.Amount);
        if (RefundedAmount > 0)
        {
            CaptureStatus = RefundedAmount >= CapturedAmount ? "REFUNDED" : "PARTIALLY_REFUNDED";
            Status = RefundedAmount >= CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        }
        Touch();
    }

    private void Touch() => ConcurrencyStamp = Guid.NewGuid().ToString("N");
}
