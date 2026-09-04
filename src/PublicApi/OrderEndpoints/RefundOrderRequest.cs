using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public RefundOrderRequest()
    {
    }

    public RefundOrderRequest(RefundOrderRequest other)
    {
        Amount = other.Amount;
        IdempotencyKey = other.IdempotencyKey;
    }

    public int OrderId { get; set; }
    public decimal Amount { get; set; }
    public string IdempotencyKey { get; set; } = Guid.NewGuid().ToString("N");
}