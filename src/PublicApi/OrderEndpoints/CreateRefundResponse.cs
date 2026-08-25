namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundResponse : BaseResponse
{
    public string RefundId { get; set; } = string.Empty;
    public string RefundStatus { get; set; } = string.Empty;
    public decimal RefundedAmount { get; set; }
    public bool AlreadyExisted { get; set; }
}
