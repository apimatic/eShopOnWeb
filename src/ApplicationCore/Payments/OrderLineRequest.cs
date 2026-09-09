namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>One line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
