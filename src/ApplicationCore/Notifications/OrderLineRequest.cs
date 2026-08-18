namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One requested order line: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
