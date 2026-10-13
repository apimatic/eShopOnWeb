using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an order asks for more units of a catalog item than eShop currently holds.
/// </summary>
public class InsufficientStockException : Exception
{
    public int CatalogItemId { get; }
    public int Available { get; }
    public int Requested { get; }

    public InsufficientStockException(int catalogItemId, int available, int requested)
        : base($"Insufficient stock for catalog item {catalogItemId}: requested {requested}, but only {available} available.")
    {
        CatalogItemId = catalogItemId;
        Available = available;
        Requested = requested;
    }
}
