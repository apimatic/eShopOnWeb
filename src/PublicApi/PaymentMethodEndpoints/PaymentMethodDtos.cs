namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public PayOrderBillingAddressRequest? BillingAddress { get; set; }
}

public class PayOrderBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "US";
}

public class PaymentMethodResponse
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class CreatePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodResponse PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public System.Collections.Generic.List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}
