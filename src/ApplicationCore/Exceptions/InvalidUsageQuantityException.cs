using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A usage report carried a quantity that is zero or negative. Rejected before any provider call
/// (plan.md UC2, "quantity is zero or negative" failure scenario).
/// </summary>
public class InvalidUsageQuantityException : Exception
{
    public InvalidUsageQuantityException(decimal quantity)
        : base($"Usage quantity must be greater than zero, but was {quantity}.")
    {
        Quantity = quantity;
    }

    public decimal Quantity { get; }
}
