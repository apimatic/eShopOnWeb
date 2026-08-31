using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    public List<CreateOrderItemDto> Items { get; set; } = new();

    [Required]
    public string ShipToStreet { get; set; } = string.Empty;

    [Required]
    public string ShipToCity { get; set; } = string.Empty;

    public string ShipToState { get; set; } = string.Empty;

    [Required]
    public string ShipToCountry { get; set; } = string.Empty;

    [Required]
    public string ShipToZipCode { get; set; } = string.Empty;
}

public class CreateOrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
