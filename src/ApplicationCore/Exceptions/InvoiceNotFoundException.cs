using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvoiceNotFoundException : Exception
{
    public InvoiceNotFoundException(string invoiceId) : base($"No invoice found with id {invoiceId}")
    {
    }
}
