using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

// Wire DTOs mirroring the Walmart OpenAPI contracts in api-specs/walmart.
// Only the fields this integration consumes or produces are modelled; the contract is the source
// of truth for shape, field names and nullability.

// ---- authorization.json : POST /auth/v4/token ----

internal sealed class WalmartTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

internal sealed class WalmartOAuthError
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; set; }
}

// ---- inventory.json : /inventory/v4/items ----

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
    public string? SellerSku { get; set; }

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

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; set; }
}

internal sealed class BulkUpdateInventoryRequest
{
    [JsonPropertyName("items")]
    public List<BulkInventoryItem> Items { get; set; } = new();
}

internal sealed class BulkInventoryItem
{
    [JsonPropertyName("sellerSku")]
    public string SellerSku { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
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

internal sealed class WalmartErrorList
{
    [JsonPropertyName("errors")]
    public List<WalmartError>? Errors { get; set; }
}

internal sealed class WalmartError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

/// <summary>Extracts the single stock quantity for a SKU from an inventory summary.</summary>
internal static class InventoryQuantityExtensions
{
    /// <summary>
    /// Resolves the on-hand quantity Walmart records for a SKU. The contract's update request
    /// declares an on-hand quantity, so on-hand is preferred; fulfillable is a fallback for
    /// servers that surface only the available-to-sell figure.
    /// </summary>
    public static int ResolveQuantity(this InventoryDetails? details)
    {
        if (details is null)
        {
            return 0;
        }
        return details.OnHandQuantity ?? details.FulfillableQuantity ?? 0;
    }
}
