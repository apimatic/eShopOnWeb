using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    public List<OrderItemRequestDto> Items { get; set; } = new();

    [Required]
    public AddressDto ShipToAddress { get; set; } = new();
}

public class OrderItemRequestDto
{
    public int CatalogItemId { get; set; }
    public int Units { get; set; }
}

public class AddressDto
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}
