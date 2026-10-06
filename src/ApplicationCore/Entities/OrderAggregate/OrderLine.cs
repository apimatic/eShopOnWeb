namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>A catalog item and how many of it to order. The price always comes from the catalog.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
