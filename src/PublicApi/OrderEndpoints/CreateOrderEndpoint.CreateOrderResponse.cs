using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse
{
    /// <summary>The identifier of the placed order. Top-level so the flow can be driven end to end.</summary>
    public int OrderId { get; init; }

    public decimal Total { get; init; }

    public IReadOnlyList<CreateOrderResponseItem> Items { get; init; } = Array.Empty<CreateOrderResponseItem>();
}

public class CreateOrderResponseItem
{
    public int CatalogItemId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal UnitPrice { get; init; }
    public int Units { get; init; }
}
