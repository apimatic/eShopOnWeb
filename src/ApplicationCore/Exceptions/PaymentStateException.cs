using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the order's current state
/// (e.g. fulfilling an unpaid order, over-refunding, an authorization that can
/// no longer be renewed). Maps to 409 at the API boundary.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
