using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation would take more units off a catalog item's eShop stock than are
/// currently held, e.g. an order for more of an item than eShop has.
/// </summary>
public class InsufficientStockException : Exception
{
    public int CatalogItemId { get; }
    public int Available { get; }
    public int Requested { get; }

    public InsufficientStockException(int catalogItemId, int available, int requested)
        : base($"Insufficient stock for catalog item {catalogItemId}: requested {requested} but only {available} in stock.")
    {
        CatalogItemId = catalogItemId;
        Available = available;
        Requested = requested;
    }
}
