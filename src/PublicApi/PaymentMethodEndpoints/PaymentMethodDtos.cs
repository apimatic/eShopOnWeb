using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public partial class SavePaymentMethodApiRequest : BaseRequest
{
    public CardDetailsRequest Card { get; set; } = new();
}

public class CardDetailsRequest
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public CardBillingAddressRequest? BillingAddress { get; set; }
}

public class CardBillingAddressRequest
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

public class PaymentMethodDto
{
    public int PaymentMethodId { get; set; }
    public string? Brand { get; set; }
    public string? LastFourDigits { get; set; }
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class SavePaymentMethodApiResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public PaymentMethodDto PaymentMethod { get; set; } = new();
}

public class ListPaymentMethodsApiResponse : BaseResponse
{
    public List<PaymentMethodDto> PaymentMethods { get; set; } = new();
}
