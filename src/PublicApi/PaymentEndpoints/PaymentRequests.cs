using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class AddressRequest
{
    public string? Street { get; set; }
    public string? Line1 { get; set; }
    public string? Address1 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
    public string? PostalCode { get; set; }
    public string? Zip { get; set; }
}

public class CardDetailsRequest
{
    public string? Number { get; set; }
    public string? CardNumber { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpMonth { get; set; }
    public int ExpiryMonth { get; set; }
    public int ExpirationYear { get; set; }
    public int ExpYear { get; set; }
    public int ExpiryYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Cvv { get; set; }
    public string? Cvc { get; set; }
    public string? Name { get; set; }
    public string? CardholderName { get; set; }
    public AddressRequest? Address { get; set; }
    public AddressRequest? BillingAddress { get; set; }
}

public class PaymentCardRequest : CardDetailsRequest
{
    public CardDetailsRequest? Card { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Id { get; set; }
    public int Quantity { get; set; }
    public int Units { get; set; }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest> Items { get; set; } = new();
    public List<int>? CatalogItemIds { get; set; }
    public AddressRequest? ShippingAddress { get; set; }
    public AddressRequest? ShipToAddress { get; set; }
}

public class PayOrderRequest : PaymentCardRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public int? SavedPaymentMethodId { get; set; }
    public int? SavedCardId { get; set; }
    public int? CardId { get; set; }
}

public class RefundOrderRequest
{
    public int OrderId { get; set; }
    public decimal? Amount { get; set; }
    public int? AmountCents { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? IdempotencyId { get; set; }
    public string? Key { get; set; }
}

public class SavePaymentMethodRequest : PaymentCardRequest
{
}

public class ReconciliationRequest
{
    public ReconciliationRequest(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    public DateTimeOffset From { get; }
    public DateTimeOffset To { get; }
}


