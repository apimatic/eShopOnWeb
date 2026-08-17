namespace Microsoft.eShopWeb.ApplicationCore.Notifications;

/// <summary>One catalog line of a placed order: a catalog item and how many of it.</summary>
public record OrderLineRequest(int CatalogItemId, int Quantity);
