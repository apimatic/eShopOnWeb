using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A usage quantity of zero or less was rejected before any provider call (UC2 failure scenarios).
/// </summary>
public class InvalidUsageQuantityException : Exception
{
    public InvalidUsageQuantityException(double quantity)
        : base($"Usage quantity must be greater than zero; received {quantity}")
    {
    }
}
