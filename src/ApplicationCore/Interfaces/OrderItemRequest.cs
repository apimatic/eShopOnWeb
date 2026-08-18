namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>A requested line when placing an order directly from catalog items (id + quantity).</summary>
public record OrderItemRequest(int CatalogItemId, int Quantity);
