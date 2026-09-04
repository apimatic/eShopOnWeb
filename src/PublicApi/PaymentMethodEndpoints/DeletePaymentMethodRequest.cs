using System;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class DeletePaymentMethodRequest : BaseRequest
{
    public DeletePaymentMethodRequest(Guid paymentMethodId)
    {
        PaymentMethodId = paymentMethodId;
    }

    public Guid PaymentMethodId { get; set; }
}