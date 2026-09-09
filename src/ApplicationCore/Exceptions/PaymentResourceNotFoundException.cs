using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when an order, payment, or saved card does not exist — or does not belong to the caller.
/// Not-owned is reported as not-found on purpose, so one shopper can never probe another's data.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message)
    {
    }
}
