namespace Microsoft.eShopWeb.ApplicationCore.Sms;

/// <summary>A catalog item and quantity requested when placing an order.</summary>
/// <param name="CatalogItemId">The catalog item being ordered.</param>
/// <param name="Quantity">How many units.</param>
public record OrderLineItem(int CatalogItemId, int Quantity);
