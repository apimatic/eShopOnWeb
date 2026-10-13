using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

// Wire models mirroring the subset of the Walmart OpenAPI schemas this integration uses.
// Property names match the contract exactly (authorization.json / inventory.json).

// ---- Authorization (authorization.json) ----

internal sealed class WalmartTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

internal sealed class WalmartOAuthError
{
    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("error_description")] public string? ErrorDescription { get; set; }
}

// ---- Inventory (inventory.json) ----

internal sealed class UpdateInventoryRequest
{
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

internal sealed class UpdateInventoryResponse
{
    [JsonPropertyName("sellerSku")] public string? SellerSku { get; set; }
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
}

internal sealed class InventoryDetails
{
    [JsonPropertyName("fulfillableQuantity")] public int? FulfillableQuantity { get; set; }
    [JsonPropertyName("onHandQuantity")] public int? OnHandQuantity { get; set; }
}

internal sealed class InventorySummary
{
    [JsonPropertyName("sellerSku")] public string? SellerSku { get; set; }
    [JsonPropertyName("inventoryDetails")] public InventoryDetails? InventoryDetails { get; set; }
}

internal sealed class Pagination
{
    [JsonPropertyName("nextToken")] public string? NextToken { get; set; }
    [JsonPropertyName("totalCount")] public int? TotalCount { get; set; }
}

internal sealed class GetInventorySummariesResponse
{
    [JsonPropertyName("pagination")] public Pagination? Pagination { get; set; }
    [JsonPropertyName("inventorySummaries")] public List<InventorySummary>? InventorySummaries { get; set; }
}

internal sealed class BulkInventoryItem
{
    [JsonPropertyName("sellerSku")] public string SellerSku { get; set; } = string.Empty;
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

internal sealed class BulkUpdateInventoryRequest
{
    [JsonPropertyName("items")] public List<BulkInventoryItem> Items { get; set; } = new();
}

internal sealed class BulkItemError
{
    [JsonPropertyName("sellerSku")] public string? SellerSku { get; set; }
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal sealed class BulkUpdateInventoryResponse
{
    [JsonPropertyName("succeeded")] public List<UpdateInventoryResponse>? Succeeded { get; set; }
    [JsonPropertyName("errors")] public List<BulkItemError>? Errors { get; set; }
}

internal sealed class WalmartError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("details")] public string? Details { get; set; }
}

internal sealed class WalmartErrorList
{
    [JsonPropertyName("errors")] public List<WalmartError>? Errors { get; set; }
}
