using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment action was requested that the order's current state does not allow
/// (e.g. fulfilling an order that was never paid, or refunding beyond what was captured).
/// Maps to a client error (409/400) at the API boundary.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
