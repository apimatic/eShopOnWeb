using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum OrderPaymentState { AwaitingPayment, Authorized, Captured, Cancelled, Refunded, PartiallyRefunded, PaymentFailed }
public class PaymentRecord : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();
    private PaymentRecord() { }
    public PaymentRecord(int orderId, string currency) { OrderId = orderId; Currency = currency; State = OrderPaymentState.AwaitingPayment; }
    public int OrderId { get; private set; }
    public string Currency { get; private set; } = null!;
    public OrderPaymentState State { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetProceeds { get; private set; }
    public string? LastError { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public void SetAuthorization(string orderId, string authorizationId, string status) { PayPalOrderId=orderId; AuthorizationId=authorizationId; AuthorizationStatus=status; State=OrderPaymentState.Authorized; LastError=null; }
    public void SetCapture(string captureId, string status, decimal amount, decimal fee, decimal net) { CaptureId=captureId; CaptureStatus=status; CapturedAmount=amount; PayPalFee=fee; NetProceeds=net; State=OrderPaymentState.Captured; LastError=null; }
    public void Cancel() { State=OrderPaymentState.Cancelled; }
    public void Fail(string error) { State=OrderPaymentState.PaymentFailed; LastError=error; }
    public void AddRefund(PaymentRefund refund) { _refunds.Add(refund); State = refund.TotalAfter == CapturedAmount ? OrderPaymentState.Refunded : OrderPaymentState.PartiallyRefunded; }
}
public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }
    public PaymentRefund(string key, string paypalId, decimal amount, decimal totalAfter, string status) { IdempotencyKey=key; PayPalRefundId=paypalId; Amount=amount; TotalAfter=totalAfter; Status=status; }
    public int PaymentRecordId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string PayPalRefundId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public decimal TotalAfter { get; private set; }
    public string Status { get; private set; } = null!;
}
