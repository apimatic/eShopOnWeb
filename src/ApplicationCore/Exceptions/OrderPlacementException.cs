using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when an order cannot be placed from the requested lines (e.g. unknown catalog
/// item or an invalid quantity).</summary>
public class OrderPlacementException : Exception
{
    public OrderPlacementException(string message) : base(message)
    {
    }
}
