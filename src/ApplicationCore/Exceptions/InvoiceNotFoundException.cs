namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class InvoiceNotFoundException : ResourceNotFoundException
{
    public InvoiceNotFoundException(string invoiceId)
        : base($"No invoice found with id {invoiceId}")
    {
    }
}
