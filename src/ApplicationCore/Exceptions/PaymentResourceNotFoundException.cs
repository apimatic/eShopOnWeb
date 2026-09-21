using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order or saved card does not exist, or does not belong to the caller. Both cases map to a
/// 404 so one shopper cannot probe another's data by id.
/// </summary>
public class PaymentResourceNotFoundException : Exception
{
    public PaymentResourceNotFoundException(string message) : base(message) { }
}
