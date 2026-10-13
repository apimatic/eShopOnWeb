using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart.Contracts;

// Schemas from api-specs/walmart/inventory.json. Only the fields this integration reads or writes
// are modelled; the inventory contract carries many more (WFS insights, reserved/researching
// breakdowns, etc.) that this stock capability does not need.

/// <summary>inventory.json #/components/schemas/UpdateInventoryRequest</summary>
internal sealed class UpdateInventoryRequest
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

/// <summary>inventory.json #/components/schemas/UpdateInventoryResponse</summary>
internal sealed class UpdateInventoryResponse
{
    [JsonPropertyName("sellerSku")]
    public string? SellerSku { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

/// <summary>inventory.json #/components/schemas/InventorySummary</summary>
internal sealed class InventorySummary
{
    [JsonPropertyName("sellerSku")]
    public string? SellerSku { get; set; }

    [JsonPropertyName("inventoryDetails")]
    public InventoryDetails? InventoryDetails { get; set; }
}

/// <summary>inventory.json #/components/schemas/InventoryDetails</summary>
internal sealed class InventoryDetails
{
    [JsonPropertyName("fulfillableQuantity")]
    public int? FulfillableQuantity { get; set; }

    [JsonPropertyName("onHandQuantity")]
    public int? OnHandQuantity { get; set; }
}

/// <summary>inventory.json #/components/schemas/GetInventorySummariesResponse</summary>
internal sealed class GetInventorySummariesResponse
{
    [JsonPropertyName("pagination")]
    public Pagination? Pagination { get; set; }

    [JsonPropertyName("inventorySummaries")]
    public List<InventorySummary>? InventorySummaries { get; set; }
}

/// <summary>inventory.json #/components/schemas/Pagination</summary>
internal sealed class Pagination
{
    [JsonPropertyName("nextToken")]
    public string? NextToken { get; set; }

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; set; }
}

/// <summary>inventory.json #/components/schemas/BulkUpdateInventoryRequest</summary>
internal sealed class BulkUpdateInventoryRequest
{
    [JsonPropertyName("items")]
    public List<BulkInventoryItem> Items { get; set; } = new();
}

/// <summary>inventory.json #/components/schemas/BulkInventoryItem</summary>
internal sealed class BulkInventoryItem
{
    [JsonPropertyName("sellerSku")]
    public string SellerSku { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

/// <summary>inventory.json #/components/schemas/BulkUpdateInventoryResponse</summary>
internal sealed class BulkUpdateInventoryResponse
{
    [JsonPropertyName("succeeded")]
    public List<UpdateInventoryResponse>? Succeeded { get; set; }

    [JsonPropertyName("errors")]
    public List<BulkItemError>? Errors { get; set; }
}

/// <summary>inventory.json #/components/schemas/BulkItemError</summary>
internal sealed class BulkItemError
{
    [JsonPropertyName("sellerSku")]
    public string? SellerSku { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

/// <summary>inventory.json #/components/schemas/ErrorList</summary>
internal sealed class ErrorList
{
    [JsonPropertyName("errors")]
    public List<ApiError>? Errors { get; set; }
}

/// <summary>inventory.json #/components/schemas/Error</summary>
internal sealed class ApiError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}
