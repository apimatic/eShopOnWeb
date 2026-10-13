using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Marketplace;

/// <summary>
/// A single SKU's stock as recorded by Walmart Marketplace, projected down to the fields eShop
/// cares about from the Walmart inventory contract.
/// </summary>
public record WalmartInventoryRecord(string Sku, int Quantity);

/// <summary>A requested absolute stock level for one SKU, to be pushed to Walmart.</summary>
public record WalmartInventoryUpdate(string Sku, int Quantity);

/// <summary>Outcome of pushing a single SKU's stock level to Walmart during a bulk sync.</summary>
public record WalmartSyncResult(string Sku, bool Succeeded);

/// <summary>Aggregate outcome of a bulk inventory update against Walmart.</summary>
public record WalmartBulkResult(IReadOnlyList<WalmartSyncResult> Results);
