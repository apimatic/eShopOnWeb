using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A requested order, payment or saved card does not exist — or does not belong to the caller.
/// Not-owned resources are reported as not-found so one shopper cannot even learn of another's.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message)
    {
    }
}
