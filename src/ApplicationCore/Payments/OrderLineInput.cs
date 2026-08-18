namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>A requested order line: a catalog item and how many of it. Prices come from the catalog, not the caller.</summary>
public record OrderLineInput(int CatalogItemId, int Quantity);
