using System;
namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderPayment : BaseEntity
{
    private OrderPayment() { }
    public OrderPayment(int orderId, string buyerId, decimal amount, string currency) { OrderId=orderId; BuyerId=buyerId; Amount=amount; Currency=currency; InvoiceId=$"ESHOP-{orderId}-{Guid.NewGuid():N}"; Status="CREATED"; }
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string PayPalOrderId { get; private set; } = null!;
    public string InvoiceId { get; private set; } = null!;
    public string AuthorizationId { get; private set; } = null!;
    public string AuthorizationStatus { get; private set; } = null!;
    public DateTimeOffset? AuthorizationExpiresUtc { get; private set; }
    public string CaptureId { get; private set; } = null!;
    public string CaptureStatus { get; private set; } = null!;
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string Status { get; private set; } = null!;
    public void Authorized(string orderId,string authId,string status,DateTimeOffset? expires) { PayPalOrderId=orderId; AuthorizationId=authId; AuthorizationStatus=status; AuthorizationExpiresUtc=expires; Status="AUTHORIZED"; }
    public void Reauthorized(string authId,string status,DateTimeOffset? expires) { AuthorizationId=authId; AuthorizationStatus=status; AuthorizationExpiresUtc=expires; }
    public void Captured(string id,string status,decimal amount,decimal? fee,decimal? net) { CaptureId=id; CaptureStatus=status; CapturedAmount=amount; PayPalFee=fee; NetAmount=net; Status="CAPTURED"; }
    public void Voided(string status) { AuthorizationStatus=status; Status="VOIDED"; }
}
