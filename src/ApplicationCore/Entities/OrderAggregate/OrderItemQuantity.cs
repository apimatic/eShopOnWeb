namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>A line requested when placing an order directly from catalog items (not a basket).</summary>
public record OrderItemQuantity(int CatalogItemId, int Quantity);
