namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A single line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
