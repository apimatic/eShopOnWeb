using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string AuthorizationId { get; set; } = string.Empty;
    public string AuthorizationStatus { get; set; } = string.Empty;
    public DateTimeOffset AuthorizationExpiry { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}
