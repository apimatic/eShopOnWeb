namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    public int OrderId { get; set; }

    /// <summary>Amount in currency units. Omit to refund the remaining captured amount.</summary>
    public decimal? Amount { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;
}
