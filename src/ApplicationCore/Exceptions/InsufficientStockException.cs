using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation would take more units off a catalog item's stock than it currently holds.
/// </summary>
public class InsufficientStockException : Exception
{
    public int CatalogItemId { get; }
    public int Available { get; }
    public int Requested { get; }

    public InsufficientStockException(int catalogItemId, int available, int requested)
        : base($"Catalog item {catalogItemId} has {available} unit(s) in stock, which is fewer than the {requested} requested.")
    {
        CatalogItemId = catalogItemId;
        Available = available;
        Requested = requested;
    }
}
