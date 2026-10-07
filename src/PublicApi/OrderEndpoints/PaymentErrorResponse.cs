namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Error body for the order/payment endpoints: a message the caller can act on plus payment context.</summary>
public class PaymentErrorResponse
{
    public int StatusCode { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string? PaymentStatus { get; set; }
    public string? ResultCode { get; set; }
    public string? RefusalReason { get; set; }
    public int? RefundId { get; set; }
}
