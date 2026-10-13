namespace Microsoft.eShopWeb.ApplicationCore.Walmart;

/// <summary>A SKU/quantity pair to push to Walmart.</summary>
public record WalmartInventoryUpdate(string Sku, int Quantity);

/// <summary>The quantity Walmart records for a SKU at a point in time.</summary>
public record WalmartInventorySnapshot(string Sku, int Quantity);

/// <summary>The outcome of setting a single SKU's quantity on Walmart.</summary>
public record WalmartInventorySetResult(string Sku, int Quantity);

/// <summary>Whether Walmart accepted a SKU's update during a sync.</summary>
public enum WalmartSyncStatus
{
    Updated,
    Failed
}

/// <summary>Per-SKU result of a sync, with an optional reason when it failed.</summary>
public record WalmartSyncResult(string Sku, WalmartSyncStatus Status, string? Message = null);
