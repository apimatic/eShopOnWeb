namespace Microsoft.eShopWeb.ApplicationCore.Invoicing;

/// <summary>A catalog item id and the quantity of it being ordered.</summary>
public record CatalogItemQuantity(int CatalogItemId, int Quantity);
