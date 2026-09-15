using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardDto Card { get; set; } = new();
}

public class CardDto
{
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public string? Name { get; set; }
    public OrderEndpoints.BillingAddressDto? BillingAddress { get; set; }
}

public class PaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string? Expiry { get; set; }
    public string? CardholderName { get; set; }
}

public class ListPaymentMethodsResponse : BaseResponse
{
    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodResponse : BaseResponse
{
    public int PaymentMethodId { get; set; }
}

internal static class PaymentMethodMapper
{
    public static PaymentMethodResponse ToDto(SavedPaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        LastDigits = method.LastDigits,
        Brand = method.Brand,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };
}
