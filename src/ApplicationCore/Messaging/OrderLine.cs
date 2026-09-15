namespace Microsoft.eShopWeb.ApplicationCore.Messaging;

/// <summary>
/// A requested order line: a catalog item and how many of it. Used when placing an order from catalog
/// item ids through the API.
/// </summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);
