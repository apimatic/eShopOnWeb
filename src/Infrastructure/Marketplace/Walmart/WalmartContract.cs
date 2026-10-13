namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Fixed values taken verbatim from Walmart's OpenAPI contract (api-specs/walmart). These are the
/// production server addresses and paths the integration builds against; the base-URL override,
/// when configured, only changes their scheme/host/port, never the paths or base paths below.
/// </summary>
internal static class WalmartContract
{
    /// <summary>
    /// Authorization API production server — from <c>authorization.json</c> <c>servers[0].url</c>.
    /// Carries the <c>/seller</c> base path.
    /// </summary>
    public const string AuthServer = "https://marketplace.walmart.com/seller";

    /// <summary>
    /// Inventory API production server — from <c>inventory.json</c> <c>servers[0].url</c>.
    /// Carries the <c>/seller/v1</c> base path.
    /// </summary>
    public const string InventoryServer = "https://marketplace.walmart.com/seller/v1";

    /// <summary>Token endpoint path — <c>authorization.json</c> <c>POST /auth/v4/token</c>.</summary>
    public const string TokenPath = "/auth/v4/token";

    /// <summary>Inventory collection path — <c>inventory.json</c> <c>/inventory/v4/items</c>.</summary>
    public const string InventoryItemsPath = "/inventory/v4/items";

    /// <summary>Bulk inventory update path — <c>inventory.json</c> <c>POST /inventory/v4/items/bulk</c>.</summary>
    public const string InventoryBulkPath = "/inventory/v4/items/bulk";

    /// <summary>Max items per bulk request — <c>inventory.json</c> <c>BulkUpdateInventoryRequest.items.maxItems</c>.</summary>
    public const int BulkMaxItems = 500;

    /// <summary>Max page size for inventory summaries — <c>inventory.json</c> <c>PageSizeParam.maximum</c>.</summary>
    public const int MaxPageSize = 500;
}
