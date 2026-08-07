using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The order is not in a state that allows the requested payment operation (e.g. refunding
/// an unpaid order, or paying a refunded one). Maps to HTTP 409.</summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
