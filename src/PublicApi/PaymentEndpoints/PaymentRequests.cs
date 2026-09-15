using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class CreateOrderRequest
{
    [Required, MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = new();
    [Required] public ShippingAddressRequest ShippingAddress { get; set; } = new();
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)] public int CatalogItemId { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    [Required] public string Street { get; set; } = string.Empty;
    [Required] public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    [Required] public string Country { get; set; } = string.Empty;
    [Required] public string ZipCode { get; set; } = string.Empty;
}

public sealed class PayOrderRequest
{
    public CardRequest? Card { get; set; }
    public int? PaymentMethodId { get; set; }
}

public sealed class SavePaymentMethodRequest
{
    [Required] public CardRequest Card { get; set; } = new();
}

public sealed class CardRequest
{
    [Required] public string Name { get; set; } = string.Empty;
    [Required] public string Number { get; set; } = string.Empty;
    [Required] public string Expiry { get; set; } = string.Empty;
    [Required] public string SecurityCode { get; set; } = string.Empty;
    [Required] public CardBillingAddressRequest BillingAddress { get; set; } = new();

    public CardDetails ToModel() => new(Name, Number.Replace(" ", string.Empty).Replace("-", string.Empty),
        Expiry, SecurityCode, BillingAddress.ToModel());
}

public sealed class CardBillingAddressRequest
{
    [Required, MinLength(2), MaxLength(2)] public string CountryCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }

    public CardBillingAddress ToModel() => new(CountryCode.ToUpperInvariant(), AddressLine1, AddressLine2,
        City, State, PostalCode);
}

public sealed class RefundOrderRequest
{
    public decimal? Amount { get; set; }
    [Required, MaxLength(100)] public string IdempotencyKey { get; set; } = string.Empty;
}
