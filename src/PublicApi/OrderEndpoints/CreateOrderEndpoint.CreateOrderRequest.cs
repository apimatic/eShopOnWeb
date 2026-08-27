using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderItemDto
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 1000)]
    public int Quantity { get; set; }
}

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1)]
    public List<CreateOrderItemDto> Items { get; set; } = new();

    public string? ShipToStreet { get; set; }
    public string? ShipToCity { get; set; }
    public string? ShipToState { get; set; }
    public string? ShipToCountry { get; set; }
    public string? ShipToZipCode { get; set; }
}
