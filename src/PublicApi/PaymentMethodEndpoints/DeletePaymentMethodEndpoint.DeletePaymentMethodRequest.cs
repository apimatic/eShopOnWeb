using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    [FromRoute(Name = "paymentMethodId")]
    public int PaymentMethodId { get; set; }
}
