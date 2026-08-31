namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Optional ship-to address for a new order. Shipping is not part of the invoicing flow, so when it is
/// omitted a placeholder is used; the fields are here so a caller who wants a real address can supply one.
/// </summary>
public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}
