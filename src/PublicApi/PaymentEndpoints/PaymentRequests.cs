using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CatalogItemQuantityRequest
{
    public int CatalogItemId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Country { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
}

public sealed class BillingAddressRequest
{
    public string AddressLine1 { get; init; } = string.Empty;
    public string? AddressLine2 { get; init; }
    public string City { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string PostalCode { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
}

public sealed class CardRequest
{
    public string Name { get; init; } = string.Empty;
    public string Number { get; init; } = string.Empty;
    public string Expiry { get; init; } = string.Empty;
    public string SecurityCode { get; init; } = string.Empty;
    public BillingAddressRequest BillingAddress { get; init; } = new();
}

public sealed class PlaceOrderRequest
{
    public IReadOnlyList<CatalogItemQuantityRequest> Items { get; init; } = [];
    public ShippingAddressRequest ShippingAddress { get; init; } = new();
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; init; }
    public int? PaymentMethodId { get; init; }
}

public sealed class SavePaymentMethodRequest
{
    public CardRequest Card { get; init; } = new();
}

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
}
