namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);

/// <summary>
/// Optional shipping address for a placed order. The order model requires an address; when the caller
/// does not supply one, sensible placeholders are used since delivery address is out of scope here.
/// </summary>
public record ShippingAddressRequest(string Street, string City, string State, string Country, string ZipCode);
