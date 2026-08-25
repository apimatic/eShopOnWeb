using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class GetPaymentMethodsResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Last4Digits { get; set; }
    public string? CardBrand { get; set; }
    public string? Expiry { get; set; }
}
