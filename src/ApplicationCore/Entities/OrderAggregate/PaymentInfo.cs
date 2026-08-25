using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentInfo : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private PaymentInfo() { }

    public PaymentInfo(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TotalRefunded { get; private set; }

    private readonly List<RefundRecord> _refunds = new();
    public IReadOnlyCollection<RefundRecord> Refunds => _refunds.AsReadOnly();

    public void SetAuthorization(string payPalOrderId, string authorizationId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
    }

    public void SetCapture(string captureId, decimal capturedAmount, decimal fee, decimal netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = netAmount;
    }

    public void AddRefund(RefundRecord refund)
    {
        _refunds.Add(refund);
        TotalRefunded += refund.Amount;
    }
}
