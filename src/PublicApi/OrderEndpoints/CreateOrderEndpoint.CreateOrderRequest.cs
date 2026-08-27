using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1)]
    public List<OrderItemRequest> Items { get; set; } = new();

    [Required]
    public string Street { get; set; } = string.Empty;

    [Required]
    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    [Required]
    public string Country { get; set; } = string.Empty;

    [Required]
    public string ZipCode { get; set; } = string.Empty;
}

public class OrderItemRequest
{
    [Required]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}
