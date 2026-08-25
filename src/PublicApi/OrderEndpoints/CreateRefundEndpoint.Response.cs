namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundResponse
{
    public string RefundId { get; set; } = "";
    public decimal RefundedAmount { get; set; }
    public string Status { get; set; } = "";
}
