namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>A requested quantity of one catalog item.</summary>
public sealed record OrderLine(int CatalogItemId, int Units);
