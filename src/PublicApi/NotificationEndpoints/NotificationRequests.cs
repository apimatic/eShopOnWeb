using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
}

public sealed class PlaceOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest Address { get; set; } = new();
}

public sealed class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public sealed class ShippingAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public sealed class ResendNotificationRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
}
