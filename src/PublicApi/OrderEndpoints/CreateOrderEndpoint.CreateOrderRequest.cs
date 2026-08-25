using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemQuantityDto> Items { get; set; } = new();
    public ShippingAddressDto? ShippingAddress { get; set; }

    /// <summary>Set from the caller's JWT identity — never trust a client-supplied value.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public record OrderItemQuantityDto(int CatalogItemId, int Quantity);

public record ShippingAddressDto(string Street, string City, string State, string Country, string ZipCode);
