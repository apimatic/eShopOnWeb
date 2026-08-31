namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A single catalog item and quantity requested when placing an order directly (i.e. without going
/// through a basket). What each item costs is taken from the catalog, not from the caller.
/// </summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);
