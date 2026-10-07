using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// An operation that the order's payment state does not allow (paying a paid order, refunding beyond what was paid).
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
