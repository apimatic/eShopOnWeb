using Microsoft.AspNetCore.Mvc;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class DeletePaymentMethodRequest
{
    [FromRoute(Name = "paymentMethodId")] public int PaymentMethodId { get; set; }
}
