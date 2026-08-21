using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public OrderEndpoints.CardDetailsRequest Card { get; set; } = new();
    public string? Alias { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string Last4 { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
    public string? Alias { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
}
