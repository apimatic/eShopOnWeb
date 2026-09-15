using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order/payment or saved card either does not exist or does not belong to the caller.
/// Both cases raise the same not-found result so one shopper cannot probe for another's data.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}
