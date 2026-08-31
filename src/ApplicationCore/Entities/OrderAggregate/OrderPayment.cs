using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(string paypalOrderId, string authorizationId,
        string authorizationStatus, decimal authorizedAmount, string currency,
        DateTimeOffset authorizedAt, DateTimeOffset? authorizationExpiresAt)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizedAt = authorizedAt;
        OriginalAuthorizedAt = authorizedAt;
        AuthorizationHonorExpiresAt = authorizedAt.AddDays(3);
        AuthorizationExpiresAt = authorizationExpiresAt;
    }

    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }
    public DateTimeOffset OriginalAuthorizedAt { get; private set; }
    public DateTimeOffset AuthorizationHonorExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds
        .Where(x => !string.Equals(x.Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                 && !string.Equals(x.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
        .Sum(x => x.Amount);

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAt = authorizedAt;
        AuthorizationHonorExpiresAt = authorizedAt.AddDays(3);
        AuthorizationExpiresAt = expiresAt;
        ReauthorizationCount++;
    }

    public void RecordCapture(string captureId, string captureStatus, decimal amount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset? capturedAt)
    {
        if (amount != AuthorizedAmount)
        {
            throw new InvalidOperationException("The full authorized amount must be captured.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    public void RecordVoid(string authorizationStatus, DateTimeOffset cancelledAt)
    {
        AuthorizationStatus = authorizationStatus;
        CancelledAt = cancelledAt;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId,
        string status, decimal amount, DateTimeOffset? createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }
        if (amount <= 0 || CapturedAmount is null || RefundedAmount + amount > CapturedAmount.Value)
        {
            throw new InvalidOperationException("The refund amount exceeds the remaining captured amount.");
        }

        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        return refund;
    }
}

public class PaymentRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string paypalRefundId,
        string status, decimal amount, DateTimeOffset? createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int Id { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset? CreatedAt { get; private set; }
}
