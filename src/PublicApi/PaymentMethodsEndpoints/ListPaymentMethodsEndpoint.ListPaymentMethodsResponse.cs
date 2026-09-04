using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentDtos;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodsEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public ListPaymentMethodsResponse() { }

    public ListPaymentMethodsResponse(Guid correlationId) : base(correlationId) { }

    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}
