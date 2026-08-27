using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1)]
    public List<OrderItemRequest> Items { get; set; } = new();

    public string Street { get; set; } = "1 Main St";
    public string City { get; set; } = "Seattle";
    public string State { get; set; } = "WA";
    public string Country { get; set; } = "USA";
    public string ZipCode { get; set; } = "98101";
}

public class OrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; } = 1;
}
