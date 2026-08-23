using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public CardRequest Card { get; set; } = new();
}

public class CardRequest
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string SecurityCode { get; set; } = string.Empty;
    public BillingAddressRequest? BillingAddress { get; set; }
}

public class BillingAddressRequest
{
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string AdminArea2 { get; set; } = string.Empty;
    public string AdminArea1 { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "US";
}

public class PaymentMethodResponse : BaseResponse
{
    public PaymentMethodResponse(Guid correlationId) : base(correlationId) { }
    public PaymentMethodResponse() { }

    public int PaymentMethodId { get; set; }
    public string LastDigits { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public string Expiry { get; set; } = string.Empty;
    public string? CardholderName { get; set; }
}

public class PaymentMethodListResponse : BaseResponse
{
    public PaymentMethodListResponse(Guid correlationId) : base(correlationId) { }
    public PaymentMethodListResponse() { }

    public List<PaymentMethodResponse> PaymentMethods { get; set; } = new();
}

public class DeletePaymentMethodRequest : BaseRequest
{
    public int PaymentMethodId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
}

public static class PaymentMethodMapper
{
    public static PaymentMethodResponse ToResponse(SavedPaymentMethod method, Guid correlationId) =>
        new(correlationId)
        {
            PaymentMethodId = method.Id,
            LastDigits = method.LastDigits,
            Brand = method.Brand,
            Expiry = method.Expiry,
            CardholderName = method.CardholderName
        };
}
