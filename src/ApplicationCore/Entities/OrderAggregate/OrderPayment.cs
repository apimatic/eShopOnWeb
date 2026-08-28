using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }

    internal OrderPayment(decimal amount, string currency, string authorizationRequestId)
    {
        Amount = amount;
        Currency = currency;
        AuthorizationRequestId = authorizationRequestId;
        AuthorizationStatus = "PENDING";
    }

    public int OrderId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string AuthorizationRequestId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    // Provider-accepted pending refunds also reserve captured funds so concurrent or later
    // requests can never make the order refundable beyond the original capture.
    public decimal RefundedAmount => _refunds.Where(x => x.Status != "FAILED").Sum(x => x.Amount);
    public decimal CompletedRefundAmount => _refunds.Where(x => x.Status == "COMPLETED").Sum(x => x.Amount);

    internal void Authorize(string paypalOrderId, string authorizationId, string status,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void Reauthorize(string authorizationId, string status,
        DateTimeOffset createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    internal void Capture(string captureId, string status, decimal amount,
        decimal paypalFee, decimal netAmount, DateTimeOffset capturedAt)
    {
        CaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = capturedAt;
        AuthorizationStatus = "CAPTURED";
    }

    internal void Void(string status) => AuthorizationStatus = status;

    public PaymentRefund StartRefund(string idempotencyKey, decimal amount)
    {
        var refund = new PaymentRefund(idempotencyKey, amount);
        _refunds.Add(refund);
        return refund;
    }

    internal void CompleteRefund(PaymentRefund refund)
    {
        if (!_refunds.Contains(refund)) _refunds.Add(refund);
    }
}
