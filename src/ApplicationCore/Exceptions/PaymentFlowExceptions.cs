using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested entity does not exist, or does not belong to the caller. We deliberately do not
/// distinguish the two so a shopper cannot probe for another shopper's orders or cards.
/// </summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string message) : base(message)
    {
    }
}

/// <summary>
/// A payment operation was requested in a state that does not allow it (e.g. fulfilling an order
/// that was never authorized, or refunding beyond what was captured).
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
