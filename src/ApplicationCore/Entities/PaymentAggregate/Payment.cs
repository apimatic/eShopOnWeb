using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Payment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private Payment() { }

    public Payment(int orderId, string buyerId, string payPalOrderId, string authorizationId,
        decimal authorizedAmount, string currency, string idempotencyKey)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = "CREATED";
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string PayPalOrderId { get; private set; }
    public string AuthorizationId { get; private set; }
    public string AuthorizationStatus { get; private set; }
    public decimal AuthorizedAmount { get; private set; }
    public string Currency { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset AuthorizedAt { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string IdempotencyKey { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds) total += r.Amount;
        return total;
    }

    public void UpdateAuthorization(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationStatus = "CREATED";
    }

    public void RecordCapture(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        AuthorizationStatus = "CAPTURED";
        CapturedAt = DateTimeOffset.UtcNow;
    }

    public void VoidAuthorization()
    {
        AuthorizationStatus = "VOIDED";
    }

    public void AddRefund(PaymentRefund refund)
    {
        _refunds.Add(refund);
    }
}
