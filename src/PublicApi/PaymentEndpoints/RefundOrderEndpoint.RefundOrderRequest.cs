using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderRequest
{
    [FromRoute(Name = "orderId")] public int OrderId { get; set; }
}
