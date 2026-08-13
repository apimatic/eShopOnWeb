namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>A requested order line: a catalog item and how many of it.</summary>
public record OrderLine(int CatalogItemId, int Quantity);
