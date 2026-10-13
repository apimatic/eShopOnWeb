namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A requested order line: which catalog item, and how many units.
/// </summary>
public readonly record struct OrderItemRequest(int CatalogItemId, int Quantity);
