namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and how many of it. Price comes from the catalog.</summary>
public sealed record OrderLine(int CatalogItemId, int Quantity);
