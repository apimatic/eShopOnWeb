using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation conflicted with the current state of the order/payment
/// (e.g. capturing an unpaid order, refunding more than was captured).
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message) : base(message)
    {
    }
}
