using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order, payment or saved card does not exist or does not belong to the caller.
/// Surfaced as a 404 so the existence of another shopper's data is never leaked.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}
