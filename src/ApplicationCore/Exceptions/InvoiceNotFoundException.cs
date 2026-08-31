using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a bill does not exist for the caller. Used both when the bill is genuinely unknown and when
/// it belongs to another shopper — the two are deliberately indistinguishable so a shopper can never probe
/// for another shopper's bills.
/// </summary>
public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string message) : base(message)
    {
    }
}
