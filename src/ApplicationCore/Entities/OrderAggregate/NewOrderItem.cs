namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record NewOrderItem(int CatalogItemId, int Units);
