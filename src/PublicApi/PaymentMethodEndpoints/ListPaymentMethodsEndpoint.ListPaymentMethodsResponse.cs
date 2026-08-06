using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<SavedCardDto> PaymentMethods { get; set; } = new();
}
