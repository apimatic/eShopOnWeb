using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

// Wire models mirroring the schemas in api-specs/walmart/inventory.json. Only the fields this
// integration reads or writes are mapped; unmapped response fields are ignored on deserialization.

internal sealed class UpdateInventoryRequest
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

internal sealed class UpdateInventoryResponse
{
    [JsonPropertyName("sellerSku")]
    public string? SellerSku { get; set; }

    [JsonPropertyName("quantity")]
    public int? Quantity { get; set; }
}

internal sealed class InventorySummary
{
    [JsonPropertyName("sellerSku")]
    public string SellerSku { get; set; } = string.Empty;

    [JsonPropertyName("inventoryDetails")]
    public InventoryDetails? InventoryDetails { get; set; }
}

internal sealed class InventoryDetails
{
    [JsonPropertyName("fulfillableQuantity")]
    public int? FulfillableQuantity { get; set; }

    [JsonPropertyName("onHandQuantity")]
    public int? OnHandQuantity { get; set; }
}

internal sealed class GetInventorySummariesResponse
{
    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }

    [JsonPropertyName("inventorySummaries")]
    public List<InventorySummary>? InventorySummaries { get; set; }
}

internal sealed class Pagination
{
    [JsonPropertyName("nextToken")]
    public string? NextToken { get; set; }
}

internal sealed class BulkInventoryItem
{
    [JsonPropertyName("sellerSku")]
    public string SellerSku { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

internal sealed class BulkUpdateInventoryRequest
{
    [JsonPropertyName("items")]
    public List<BulkInventoryItem> Items { get; set; } = new();
}

internal sealed class BulkUpdateInventoryResponse
{
    [JsonPropertyName("succeeded")]
    public List<UpdateInventoryResponse>? Succeeded { get; set; }

    [JsonPropertyName("errors")]
    public List<BulkItemError>? Errors { get; set; }
}

internal sealed class BulkItemError
{
    [JsonPropertyName("sellerSku")]
    public string? SellerSku { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

// Shared error envelope (ErrorList/Error) used by the inventory error responses.
internal sealed class ErrorList
{
    [JsonPropertyName("errors")]
    public List<ErrorEntry>? Errors { get; set; }
}

internal sealed class ErrorEntry
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
