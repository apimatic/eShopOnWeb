using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest : BaseRequest
{
    [Required]
    [MinLength(1)]
    public List<CreateOrderItemRequest> Items { get; set; } = new();

    /// <summary>
    /// Optional shipping address; a placeholder is used when omitted (the API flow
    /// carries no storefront checkout form).
    /// </summary>
    public CreateOrderAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)]
    public int CatalogItemId { get; set; }

    [Range(1, 10000)]
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
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
