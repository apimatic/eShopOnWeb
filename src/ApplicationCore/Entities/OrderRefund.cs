namespace Microsoft.eShopWeb.ApplicationCore.Entities;
public class OrderRefund : BaseEntity
{
    private OrderRefund() { }
    public OrderRefund(int orderId,string buyerId,string key,decimal amount) { OrderId=orderId; BuyerId=buyerId; IdempotencyKey=key; Amount=amount; }
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string PayPalRefundId { get; private set; } = null!;
    public string Status { get; private set; } = "PENDING";
    public void Completed(string id,string status) { PayPalRefundId=id; Status=status; }
}
