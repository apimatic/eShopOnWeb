using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    /// <summary>The catalog items and quantities to order. The caller's identity comes from the token.</summary>
    [Required]
    public List<PlaceOrderItem> Items { get; set; } = new();
}

public class PlaceOrderItem
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}
