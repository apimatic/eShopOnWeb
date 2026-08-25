using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListPaymentMethodsResponse()
    {
    }

    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
